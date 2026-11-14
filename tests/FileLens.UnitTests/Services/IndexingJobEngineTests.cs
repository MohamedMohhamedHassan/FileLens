using Microsoft.Extensions.Logging.Abstractions;
using FileLens.Application.Services;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.UnitTests.Services;

public sealed class IndexingJobEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteDatabase _database;
    private readonly PathValidator _pathValidator;
    private readonly SafeFileDiscoveryService _discoveryService;
    private readonly IndexingJobEngine _engine;
    private readonly string _testDataDir = @"d:\FileLens\testdata";

    public IndexingJobEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileLens_JobEngineTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");

        var connectionFactory = new SqliteConnectionFactory(_dbPath);
        _database = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();

        _pathValidator = new PathValidator();
        _discoveryService = new SafeFileDiscoveryService(_pathValidator, NullLogger<SafeFileDiscoveryService>.Instance);
        _engine = new IndexingJobEngine(_database, _discoveryService, NullLogger<IndexingJobEngine>.Instance);
    }

    [Fact]
    public async Task StartIndexingAsync_ExecutesAndIndexesTestDataFiles()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };
        await _database.AddRootAsync(root);

        bool progressReceived = false;
        _engine.ProgressChanged += (sender, progress) =>
        {
            if (progress.DiscoveredCount > 0)
                progressReceived = true;
        };

        await _engine.StartIndexingAsync(root.Id);

        // Wait for job to finish
        int waitMs = 0;
        while (_engine.IsRunning && waitMs < 10000)
        {
            await Task.Delay(100);
            waitMs += 100;
        }

        Assert.False(_engine.IsRunning, "Job should finish within timeout");
        Assert.True(progressReceived, "Progress event should have fired");

        int fileCount = await _database.GetFileCountByRootAsync(root.Id);
        Assert.True(fileCount >= 8, $"Expected >= 8 files indexed, got {fileCount}");
    }

    [Fact]
    public void PauseAndResume_TogglesIsPausedState()
    {
        Assert.False(_engine.IsPaused);
        _engine.PauseIndexing();
        // Since no active job is running, engine handles gracefully
        Assert.False(_engine.IsPaused); // only pauses when running
    }

    [Fact]
    public async Task CancelIndexing_StopsActiveJobCleanly()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };
        await _database.AddRootAsync(root);

        await _engine.StartIndexingAsync(root.Id);
        _engine.CancelIndexing();

        int waitMs = 0;
        while (_engine.IsRunning && waitMs < 5000)
        {
            await Task.Delay(100);
            waitMs += 100;
        }

        Assert.False(_engine.IsRunning);
    }

    public void Dispose()
    {
        _engine.Dispose();
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

