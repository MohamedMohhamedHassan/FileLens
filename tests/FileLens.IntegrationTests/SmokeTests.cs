using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FileLens.Application.Configuration;
using FileLens.Application.Services;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.IntegrationTests;

public sealed class SmokeTests : IDisposable
{
    private readonly string _tempDir;

    public SmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileLens_SmokeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Configuration_CanBeLoadedFromAppSettingsJson()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", optional: false)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<FileLensOptions>(options =>
        {
            var section = configuration.GetSection(FileLensOptions.SectionName);
            var cache = section["CacheDirectory"] ?? string.Empty;
            var log = section["LogDirectory"] ?? string.Empty;
            options.CacheDirectory = Environment.ExpandEnvironmentVariables(cache);
            options.LogDirectory = Environment.ExpandEnvironmentVariables(log);
            if (int.TryParse(section["MaxConcurrency"], out var mc))
                options.MaxConcurrency = mc;
            if (long.TryParse(section["MaxFileSizeBytes"], out var ms))
                options.MaxFileSizeBytes = ms;
        });
        services.AddSingleton<IValidateOptions<FileLensOptions>, FileLensOptionsValidator>();

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<FileLensOptions>>().CurrentValue;

        Assert.NotNull(options.CacheDirectory);
        Assert.NotNull(options.LogDirectory);
        Assert.Equal(2, options.MaxConcurrency);
        Assert.Equal(524288000L, options.MaxFileSizeBytes);
    }

    [Fact]
    public async Task HealthCheck_WorksThroughDI()
    {
        var cacheDir = Path.Combine(_tempDir, "cache2");
        var logDir = Path.Combine(_tempDir, "logs2");

        var services = new ServiceCollection();
        services.Configure<FileLensOptions>(options =>
        {
            options.CacheDirectory = cacheDir;
            options.LogDirectory = logDir;
            options.MaxConcurrency = 4;
            options.MaxFileSizeBytes = 1000;
        });
        services.AddSingleton<IValidateOptions<FileLensOptions>, FileLensOptionsValidator>();
        services.AddSingleton<IAiWorkerHealthCheck, AiWorkerHealthCheckService>();

        using var provider = services.BuildServiceProvider();

        var healthCheck = provider.GetRequiredService<IAiWorkerHealthCheck>();
        var result = await healthCheck.CheckHealthAsync();

        Assert.False(result.IsHealthy);
        Assert.Equal("Stub", result.WorkerType);
        Assert.Equal("Python worker not configured", result.Message);
    }

    [Fact]
    public async Task EndToEnd_AddRoot_IndexFiles_QueryMetadata_And_RemoveRootSafely()
    {
        string testDataDir = @"d:\FileLens\testdata";
        string dbPath = Path.Combine(_tempDir, "e2e_test.db");

        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var database = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        await database.InitializeAsync();

        var pathValidator = new PathValidator();
        var discoveryService = new SafeFileDiscoveryService(pathValidator, NullLogger<SafeFileDiscoveryService>.Instance);
        var rootService = new RootManagementService(database, pathValidator, NullLogger<RootManagementService>.Instance);
        using var engine = new IndexingJobEngine(database, discoveryService, NullLogger<IndexingJobEngine>.Instance);

        // 1. Add synthetic testdata as root
        var addResult = await rootService.ValidateAndAddRootAsync(testDataDir);
        Assert.True(addResult.IsValid);

        var roots = await rootService.GetAllRootsAsync();
        Assert.Equal(1, roots.Count);
        var rootId = roots[0].Id;

        // 2. Run Indexing Job
        await engine.StartIndexingAsync(rootId);

        // Wait for job to finish
        int waited = 0;
        while (engine.IsRunning && waited < 10000)
        {
            await Task.Delay(100);
            waited += 100;
        }

        Assert.False(engine.IsRunning);

        // 3. Verify Indexed Files in SQLite
        int count = await database.GetFileCountByRootAsync(rootId);
        Assert.True(count >= 8, $"Expected at least 8 indexed files, got {count}");

        var files = await database.GetFilesByRootAsync(rootId, limit: 100);
        Assert.True(files.Count >= 8);

        // Check relative paths and extensions
        var readme = files.FirstOrDefault(f => f.FileName == "readme.txt");
        Assert.NotNull(readme);
        Assert.Equal(".txt", readme!.Extension);
        Assert.True(readme!.SizeBytes > 0);

        // 4. Verify Statistics
        var stats = await database.GetStatisticsAsync();
        Assert.Equal(1, stats.TotalRoots);
        Assert.Equal(1, stats.EnabledRoots);
        Assert.True(stats.TotalFilesIndexed >= 8);

        // 5. Remove Root and verify physical files are intact
        await rootService.RemoveRootAsync(rootId);

        var remainingRoots = await rootService.GetAllRootsAsync();
        Assert.Equal(0, remainingRoots.Count);

        int remainingFiles = await database.GetFileCountByRootAsync(rootId);
        Assert.Equal(0, remainingFiles);

        // Verify testdata directory was NEVER modified or deleted
        Assert.True(Directory.Exists(testDataDir));
        Assert.True(File.Exists(Path.Combine(testDataDir, "docs", "readme.txt")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
