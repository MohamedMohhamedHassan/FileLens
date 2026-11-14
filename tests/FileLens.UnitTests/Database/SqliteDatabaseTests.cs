using Microsoft.Extensions.Logging.Abstractions;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;

namespace FileLens.UnitTests.Database;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteDatabase _database;

    public SqliteDatabaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileLens_DbTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");

        var connectionFactory = new SqliteConnectionFactory(_dbPath);
        _database = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task InitializeAsync_SecondCallIsIdempotent()
    {
        // Calling initialize again should not throw and should keep schema intact
        await _database.InitializeAsync();
        var roots = await _database.GetAllRootsAsync();
        Assert.NotNull(roots);
    }

    [Fact]
    public async Task AddRoot_And_GetRootById_RetrievesAccurately()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = @"C:\TestFolder",
            NormalizedPath = @"c:\testfolder",
            RootType = RootType.LocalFolder,
            IsEnabled = true,
            Status = RootStatus.Idle,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _database.AddRootAsync(root);

        var retrieved = await _database.GetRootByIdAsync(root.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(root.Path, retrieved!.Path);
        Assert.Equal(root.NormalizedPath, retrieved.NormalizedPath);
        Assert.Equal(RootType.LocalFolder, retrieved.RootType);
        Assert.True(retrieved.IsEnabled);
    }

    [Fact]
    public async Task UpsertFilesBatchAsync_PreventsDuplicateRecords()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = @"C:\Folder",
            NormalizedPath = @"c:\folder",
            RootType = RootType.LocalFolder
        };
        await _database.AddRootAsync(root);

        var file1 = new IndexedFile
        {
            Id = FileEntryId.New(),
            RootId = root.Id,
            FullPath = @"C:\Folder\doc.txt",
            NormalizedPath = @"c:\folder\doc.txt",
            RelativePath = "doc.txt",
            FileName = "doc.txt",
            Extension = ".txt",
            SizeBytes = 100,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
            LastIndexedUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };

        // First insert
        await _database.UpsertFilesBatchAsync(new[] { file1 });
        int count = await _database.GetFileCountByRootAsync(root.Id);
        Assert.Equal(1, count);

        // Update same file with new size
        var file1Updated = new IndexedFile
        {
            Id = FileEntryId.New(), // Different ID, but same NormalizedPath
            RootId = root.Id,
            FullPath = @"C:\Folder\doc.txt",
            NormalizedPath = @"c:\folder\doc.txt",
            RelativePath = "doc.txt",
            FileName = "doc.txt",
            Extension = ".txt",
            SizeBytes = 500, // Updated size
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
            LastIndexedUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };

        await _database.UpsertFilesBatchAsync(new[] { file1Updated });

        // Count must still be 1 (upsert updated existing row, did not duplicate!)
        int countAfter = await _database.GetFileCountByRootAsync(root.Id);
        Assert.Equal(1, countAfter);

        var files = await _database.GetFilesByRootAsync(root.Id);
        Assert.Equal(1, files.Count);
        Assert.Equal(500L, files[0].SizeBytes);
    }

    [Fact]
    public async Task DeleteRootAsync_CascadesDeletionToIndexedFiles()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = @"C:\CascadeRoot",
            NormalizedPath = @"c:\cascaderoot",
            RootType = RootType.LocalFolder
        };
        await _database.AddRootAsync(root);

        var file = new IndexedFile
        {
            Id = FileEntryId.New(),
            RootId = root.Id,
            FullPath = @"C:\CascadeRoot\f.txt",
            NormalizedPath = @"c:\cascaderoot\f.txt",
            RelativePath = "f.txt",
            FileName = "f.txt",
            Extension = ".txt",
            SizeBytes = 42,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
            LastIndexedUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };
        await _database.UpsertFilesBatchAsync(new[] { file });

        Assert.Equal(1, await _database.GetFileCountByRootAsync(root.Id));

        // Delete root
        await _database.DeleteRootAsync(root.Id);

        // Root should be gone
        var retrievedRoot = await _database.GetRootByIdAsync(root.Id);
        Assert.Null(retrievedRoot);

        // Indexed files should be cascade deleted
        int remainingFiles = await _database.GetFileCountByRootAsync(root.Id);
        Assert.Equal(0, remainingFiles);
    }

    [Fact]
    public async Task RecoverInterruptedJobsAsync_ResetsRunningJobsAndRoots()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = @"C:\InterruptedRoot",
            NormalizedPath = @"c:\interruptedroot",
            RootType = RootType.LocalFolder,
            Status = RootStatus.Indexing
        };
        await _database.AddRootAsync(root);

        var job = new IndexingJob
        {
            Id = Guid.NewGuid(),
            RootId = root.Id,
            JobType = JobType.FullScan,
            Status = JobStatus.Running,
            StartedAtUtc = DateTime.UtcNow
        };
        await _database.CreateJobAsync(job);

        // Run recovery
        await _database.RecoverInterruptedJobsAsync();

        // Root should now be Idle
        var recoveredRoot = await _database.GetRootByIdAsync(root.Id);
        Assert.NotNull(recoveredRoot);
        Assert.Equal(RootStatus.Idle, recoveredRoot!.Status);
    }

    [Fact]
    public async Task GetStatisticsAsync_CalculatesCorrectTotals()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = @"C:\StatsRoot",
            NormalizedPath = @"c:\statsroot",
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };
        await _database.AddRootAsync(root);

        var file = new IndexedFile
        {
            Id = FileEntryId.New(),
            RootId = root.Id,
            FullPath = @"C:\StatsRoot\file.bin",
            NormalizedPath = @"c:\statsroot\file.bin",
            RelativePath = "file.bin",
            FileName = "file.bin",
            Extension = ".bin",
            SizeBytes = 2048,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
            LastIndexedUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };
        await _database.UpsertFilesBatchAsync(new[] { file });

        var stats = await _database.GetStatisticsAsync();
        Assert.True(stats.TotalRoots >= 1);
        Assert.True(stats.TotalFilesIndexed >= 1);
        Assert.True(stats.TotalSizeBytes >= 2048);
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

