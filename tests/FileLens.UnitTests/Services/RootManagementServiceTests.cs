using Microsoft.Extensions.Logging.Abstractions;
using FileLens.Application.Services;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.UnitTests.Services;

public sealed class RootManagementServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteDatabase _database;
    private readonly PathValidator _pathValidator;
    private readonly RootManagementService _service;

    public RootManagementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileLens_RootMgmtTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");

        var connectionFactory = new SqliteConnectionFactory(_dbPath);
        _database = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();

        _pathValidator = new PathValidator();
        _service = new RootManagementService(_database, _pathValidator, NullLogger<RootManagementService>.Instance);
    }

    [Fact]
    public async Task AddRoot_ValidDirectory_SucceedsAndStoresInDatabase()
    {
        string testFolder = Path.Combine(_tempDir, "SampleRoot");
        Directory.CreateDirectory(testFolder);

        var result = await _service.ValidateAndAddRootAsync(testFolder);

        Assert.True(result.IsValid);
        var roots = await _service.GetAllRootsAsync();
        Assert.Equal(1, roots.Count);
        Assert.Equal(testFolder, roots[0].Path);
        Assert.True(roots[0].IsEnabled);
    }

    [Fact]
    public async Task AddRoot_DuplicateNormalizedPath_RejectsSecondAddition()
    {
        string testFolder = Path.Combine(_tempDir, "DuplicateRoot");
        Directory.CreateDirectory(testFolder);

        var first = await _service.ValidateAndAddRootAsync(testFolder);
        Assert.True(first.IsValid);

        // Add same path with different casing / trailing slash
        var second = await _service.ValidateAndAddRootAsync(testFolder + "\\");
        Assert.False(second.IsValid);
        Assert.Contains("already configured", second.FailureReason);

        var roots = await _service.GetAllRootsAsync();
        Assert.Equal(1, roots.Count);
    }

    [Fact]
    public async Task RemoveRoot_RemovesRecord_PreservesSourceFilesOnDisk()
    {
        string testFolder = Path.Combine(_tempDir, "FolderToUnlink");
        Directory.CreateDirectory(testFolder);
        string sampleFile = Path.Combine(testFolder, "keep_me.txt");
        File.WriteAllText(sampleFile, "Data that must not be deleted");

        var result = await _service.ValidateAndAddRootAsync(testFolder);
        Assert.True(result.IsValid);

        var roots = await _service.GetAllRootsAsync();
        Assert.Equal(1, roots.Count);

        // Remove root
        await _service.RemoveRootAsync(roots[0].Id);

        var remainingRoots = await _service.GetAllRootsAsync();
        Assert.Equal(0, remainingRoots.Count);

        // CRITICAL SAFETY CHECK: Original directory and file MUST still exist!
        Assert.True(Directory.Exists(testFolder), "Directory must not be deleted when removing root");
        Assert.True(File.Exists(sampleFile), "File must not be deleted when removing root");
    }

    [Fact]
    public async Task ToggleRoot_EnablesAndDisablesRoot()
    {
        string testFolder = Path.Combine(_tempDir, "ToggleRoot");
        Directory.CreateDirectory(testFolder);

        await _service.ValidateAndAddRootAsync(testFolder);
        var roots = await _service.GetAllRootsAsync();
        var rootId = roots[0].Id;

        // Disable
        await _service.ToggleRootAsync(rootId, false);
        var updated = await _database.GetRootByIdAsync(rootId);
        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);

        // Re-enable
        await _service.ToggleRootAsync(rootId, true);
        updated = await _database.GetRootByIdAsync(rootId);
        Assert.NotNull(updated);
        Assert.True(updated!.IsEnabled);
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

