using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database.Migrations;

namespace FileLens.Infrastructure.Database;

public sealed class SqliteDatabase : IIndexDatabase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteDatabase> _logger;

    public SqliteDatabase(SqliteConnectionFactory connectionFactory, ILogger<SqliteDatabase> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SQLite database migrations...");
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        // 1. Ensure SchemaMigrations table exists
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    Description TEXT NOT NULL,
                    AppliedAtUtc TEXT NOT NULL
                );";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Read latest applied version
        int currentVersion = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
            {
                currentVersion = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        // 3. Apply pending migrations
        if (currentVersion < Migration001_InitialSchema.Version)
        {
            _logger.LogInformation("Applying Migration {Version}: {Description}", Migration001_InitialSchema.Version, Migration001_InitialSchema.Description);
            await Migration001_InitialSchema.ApplyAsync(connection, transaction, cancellationToken);

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO SchemaMigrations (Version, Description, AppliedAtUtc) VALUES ($v, $desc, $applied);";
                cmd.Parameters.AddWithValue("$v", Migration001_InitialSchema.Version);
                cmd.Parameters.AddWithValue("$desc", Migration001_InitialSchema.Description);
                cmd.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Database initialization complete. Current schema version: {Version}", Math.Max(currentVersion, Migration001_InitialSchema.Version));
    }

    public async Task<IReadOnlyList<SearchRoot>> GetAllRootsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT r.Id, r.Path, r.NormalizedPath, r.RootType, r.IsEnabled, r.Status, r.LastScanUtc, r.CreatedAtUtc,
                   (SELECT COUNT(*) FROM IndexedFiles f WHERE f.RootId = r.Id) AS FileCount
            FROM IndexRoots r
            ORDER BY r.CreatedAtUtc ASC;";

        var list = new List<SearchRoot>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadSearchRoot(reader));
        }

        return list;
    }

    public async Task<SearchRoot?> GetRootByIdAsync(SearchRootId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT r.Id, r.Path, r.NormalizedPath, r.RootType, r.IsEnabled, r.Status, r.LastScanUtc, r.CreatedAtUtc,
                   (SELECT COUNT(*) FROM IndexedFiles f WHERE f.RootId = r.Id) AS FileCount
            FROM IndexRoots r
            WHERE r.Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.Value.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSearchRoot(reader) : null;
    }

    public async Task<SearchRoot?> GetRootByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT r.Id, r.Path, r.NormalizedPath, r.RootType, r.IsEnabled, r.Status, r.LastScanUtc, r.CreatedAtUtc,
                   (SELECT COUNT(*) FROM IndexedFiles f WHERE f.RootId = r.Id) AS FileCount
            FROM IndexRoots r
            WHERE r.NormalizedPath = $p;";
        cmd.Parameters.AddWithValue("$p", normalizedPath);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSearchRoot(reader) : null;
    }

    public async Task AddRootAsync(SearchRoot root, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO IndexRoots (Id, Path, NormalizedPath, RootType, IsEnabled, Status, LastScanUtc, CreatedAtUtc)
            VALUES ($id, $path, $norm, $type, $enabled, $status, $lastScan, $created);";

        cmd.Parameters.AddWithValue("$id", root.Id.Value.ToString());
        cmd.Parameters.AddWithValue("$path", root.Path);
        cmd.Parameters.AddWithValue("$norm", root.NormalizedPath);
        cmd.Parameters.AddWithValue("$type", (int)root.RootType);
        cmd.Parameters.AddWithValue("$enabled", root.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", (int)root.Status);
        cmd.Parameters.AddWithValue("$lastScan", root.LastScanUtc.HasValue ? root.LastScanUtc.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$created", root.CreatedAtUtc.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateRootAsync(SearchRoot root, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE IndexRoots
            SET IsEnabled = $enabled,
                Status = $status,
                LastScanUtc = $lastScan
            WHERE Id = $id;";

        cmd.Parameters.AddWithValue("$id", root.Id.Value.ToString());
        cmd.Parameters.AddWithValue("$enabled", root.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", (int)root.Status);
        cmd.Parameters.AddWithValue("$lastScan", root.LastScanUtc.HasValue ? root.LastScanUtc.Value.ToString("o") : (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteRootAsync(SearchRootId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        // Deleting from IndexRoots cascades to IndexedFiles, IndexJobs, IndexJobProgress, IndexErrors
        cmd.CommandText = "DELETE FROM IndexRoots WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.Value.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertFilesBatchAsync(IReadOnlyList<IndexedFile> files, CancellationToken cancellationToken = default)
    {
        if (files == null || files.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO IndexedFiles (
                Id, RootId, FullPath, NormalizedPath, RelativePath, FileName, Extension,
                SizeBytes, CreatedAtUtc, ModifiedAtUtc, Attributes, LastIndexedUtc, Status
            )
            VALUES (
                $id, $rootId, $fullPath, $normPath, $relPath, $fileName, $ext,
                $size, $created, $modified, $attrs, $indexed, $status
            )
            ON CONFLICT(NormalizedPath) DO UPDATE SET
                SizeBytes = excluded.SizeBytes,
                ModifiedAtUtc = excluded.ModifiedAtUtc,
                Attributes = excluded.Attributes,
                LastIndexedUtc = excluded.LastIndexedUtc,
                Status = excluded.Status;";

        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pRootId = cmd.Parameters.Add("$rootId", SqliteType.Text);
        var pFullPath = cmd.Parameters.Add("$fullPath", SqliteType.Text);
        var pNormPath = cmd.Parameters.Add("$normPath", SqliteType.Text);
        var pRelPath = cmd.Parameters.Add("$relPath", SqliteType.Text);
        var pFileName = cmd.Parameters.Add("$fileName", SqliteType.Text);
        var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pCreated = cmd.Parameters.Add("$created", SqliteType.Text);
        var pModified = cmd.Parameters.Add("$modified", SqliteType.Text);
        var pAttrs = cmd.Parameters.Add("$attrs", SqliteType.Integer);
        var pIndexed = cmd.Parameters.Add("$indexed", SqliteType.Text);
        var pStatus = cmd.Parameters.Add("$status", SqliteType.Integer);

        foreach (var file in files)
        {
            pId.Value = file.Id.Value.ToString();
            pRootId.Value = file.RootId.Value.ToString();
            pFullPath.Value = file.FullPath;
            pNormPath.Value = file.NormalizedPath;
            pRelPath.Value = file.RelativePath;
            pFileName.Value = file.FileName;
            pExt.Value = file.Extension;
            pSize.Value = file.SizeBytes;
            pCreated.Value = file.CreatedAtUtc.ToString("o");
            pModified.Value = file.ModifiedAtUtc.ToString("o");
            pAttrs.Value = (int)file.Attributes;
            pIndexed.Value = file.LastIndexedUtc.ToString("o");
            pStatus.Value = (int)file.Status;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IndexedFile>> GetFilesByRootAsync(SearchRootId rootId, int limit = 1000, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, RootId, FullPath, NormalizedPath, RelativePath, FileName, Extension,
                   SizeBytes, CreatedAtUtc, ModifiedAtUtc, Attributes, LastIndexedUtc, Status
            FROM IndexedFiles
            WHERE RootId = $rootId
            LIMIT $limit;";
        cmd.Parameters.AddWithValue("$rootId", rootId.Value.ToString());
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<IndexedFile>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadIndexedFile(reader));
        }

        return list;
    }

    public async Task<int> GetFileCountByRootAsync(SearchRootId rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM IndexedFiles WHERE RootId = $rootId;";
        cmd.Parameters.AddWithValue("$rootId", rootId.Value.ToString());

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task CreateJobAsync(IndexingJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO IndexJobs (Id, RootId, JobType, Status, StartedAtUtc, CompletedAtUtc, ErrorMessage)
            VALUES ($id, $rootId, $type, $status, $start, $complete, $err);";

        cmd.Parameters.AddWithValue("$id", job.Id.ToString());
        cmd.Parameters.AddWithValue("$rootId", job.RootId.Value.ToString());
        cmd.Parameters.AddWithValue("$type", (int)job.JobType);
        cmd.Parameters.AddWithValue("$status", (int)job.Status);
        cmd.Parameters.AddWithValue("$start", job.StartedAtUtc.HasValue ? job.StartedAtUtc.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$complete", job.CompletedAtUtc.HasValue ? job.CompletedAtUtc.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$err", job.ErrorMessage ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE IndexJobs
            SET Status = $status,
                CompletedAtUtc = CASE WHEN $status IN (4, 5, 6) THEN $completed ELSE CompletedAtUtc END,
                ErrorMessage = COALESCE($err, ErrorMessage)
            WHERE Id = $id;";

        cmd.Parameters.AddWithValue("$id", jobId.ToString());
        cmd.Parameters.AddWithValue("$status", (int)status);
        cmd.Parameters.AddWithValue("$completed", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$err", errorMessage ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveJobProgressAsync(JobProgress progress, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO IndexJobProgress (
                JobId, RootId, CurrentDirectory, CurrentFile, DiscoveredCount, ProcessedCount,
                SkippedCount, FailedCount, LastUpdatedUtc
            )
            VALUES (
                $jobId, $rootId, $dir, $file, $disc, $proc, $skip, $fail, $lastUp
            )
            ON CONFLICT(JobId) DO UPDATE SET
                CurrentDirectory = excluded.CurrentDirectory,
                CurrentFile = excluded.CurrentFile,
                DiscoveredCount = excluded.DiscoveredCount,
                ProcessedCount = excluded.ProcessedCount,
                SkippedCount = excluded.SkippedCount,
                FailedCount = excluded.FailedCount,
                LastUpdatedUtc = excluded.LastUpdatedUtc;";

        cmd.Parameters.AddWithValue("$jobId", progress.JobId.ToString());
        cmd.Parameters.AddWithValue("$rootId", progress.RootId.Value.ToString());
        cmd.Parameters.AddWithValue("$dir", progress.CurrentDirectory ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$file", progress.CurrentFile ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$disc", progress.DiscoveredCount);
        cmd.Parameters.AddWithValue("$proc", progress.ProcessedCount);
        cmd.Parameters.AddWithValue("$skip", progress.SkippedCount);
        cmd.Parameters.AddWithValue("$fail", progress.FailedCount);
        cmd.Parameters.AddWithValue("$lastUp", progress.LastUpdatedUtc.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task LogErrorAsync(IndexErrorRecord error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO IndexErrors (Id, JobId, RootId, FilePath, ErrorCategory, Message, LoggedAtUtc)
            VALUES ($id, $jobId, $rootId, $path, $cat, $msg, $logged);";

        cmd.Parameters.AddWithValue("$id", error.Id.ToString());
        cmd.Parameters.AddWithValue("$jobId", error.JobId.ToString());
        cmd.Parameters.AddWithValue("$rootId", error.RootId.Value.ToString());
        cmd.Parameters.AddWithValue("$path", error.FilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", error.ErrorCategory);
        cmd.Parameters.AddWithValue("$msg", error.Message);
        cmd.Parameters.AddWithValue("$logged", error.LoggedAtUtc.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IndexErrorRecord>> GetRecentErrorsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, JobId, RootId, FilePath, ErrorCategory, Message, LoggedAtUtc
            FROM IndexErrors
            ORDER BY LoggedAtUtc DESC
            LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<IndexErrorRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new IndexErrorRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                JobId = Guid.Parse(reader.GetString(1)),
                RootId = new SearchRootId(Guid.Parse(reader.GetString(2))),
                FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                ErrorCategory = reader.GetString(4),
                Message = reader.GetString(5),
                LoggedAtUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind)
            });
        }

        return list;
    }

    public async Task<IndexingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM IndexRoots) AS TotalRoots,
                (SELECT COUNT(*) FROM IndexRoots WHERE IsEnabled = 1) AS EnabledRoots,
                (SELECT COUNT(*) FROM IndexedFiles) AS TotalFiles,
                (SELECT COALESCE(SUM(SizeBytes), 0) FROM IndexedFiles) AS TotalSize,
                (SELECT COUNT(*) FROM IndexErrors) AS TotalErrors,
                (SELECT MAX(LastScanUtc) FROM IndexRoots) AS LastScan;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            int totalRoots = reader.GetInt32(0);
            int enabledRoots = reader.GetInt32(1);
            int totalFiles = reader.GetInt32(2);
            long totalSize = reader.GetInt64(3);
            int totalErrors = reader.GetInt32(4);
            DateTime? lastScan = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind);

            return new IndexingStatistics(totalRoots, enabledRoots, totalFiles, totalSize, totalErrors, lastScan);
        }

        return new IndexingStatistics(0, 0, 0, 0, 0, null);
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking for interrupted indexing jobs to recover...");
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        // 1. Cancel any jobs left in Running (2) or Paused (3)
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE IndexJobs
                SET Status = $cancelled,
                    CompletedAtUtc = $now,
                    ErrorMessage = 'Interrupted by application restart'
                WHERE Status IN ($running, $paused);";

            cmd.Parameters.AddWithValue("$cancelled", (int)JobStatus.Cancelled);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$running", (int)JobStatus.Running);
            cmd.Parameters.AddWithValue("$paused", (int)JobStatus.Paused);

            int recoveredJobs = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (recoveredJobs > 0)
            {
                _logger.LogWarning("Recovered {Count} interrupted jobs left in active state", recoveredJobs);
            }
        }

        // 2. Reset any roots left in Indexing (2) or Paused (3) back to Idle (1)
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE IndexRoots
                SET Status = $idle
                WHERE Status IN ($indexing, $paused);";

            cmd.Parameters.AddWithValue("$idle", (int)RootStatus.Idle);
            cmd.Parameters.AddWithValue("$indexing", (int)RootStatus.Indexing);
            cmd.Parameters.AddWithValue("$paused", (int)RootStatus.Paused);

            int resetRoots = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (resetRoots > 0)
            {
                _logger.LogInformation("Reset {Count} roots back to Idle state", resetRoots);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static SearchRoot ReadSearchRoot(SqliteDataReader reader)
    {
        return new SearchRoot
        {
            Id = new SearchRootId(Guid.Parse(reader.GetString(0))),
            Path = reader.GetString(1),
            NormalizedPath = reader.GetString(2),
            RootType = (RootType)reader.GetInt32(3),
            IsEnabled = reader.GetInt32(4) == 1,
            Status = (RootStatus)reader.GetInt32(5),
            LastScanUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
            CreatedAtUtc = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
            FileCount = reader.GetInt32(8)
        };
    }

    private static IndexedFile ReadIndexedFile(SqliteDataReader reader)
    {
        return new IndexedFile
        {
            Id = new FileEntryId(Guid.Parse(reader.GetString(0))),
            RootId = new SearchRootId(Guid.Parse(reader.GetString(1))),
            FullPath = reader.GetString(2),
            NormalizedPath = reader.GetString(3),
            RelativePath = reader.GetString(4),
            FileName = reader.GetString(5),
            Extension = reader.GetString(6),
            SizeBytes = reader.GetInt64(7),
            CreatedAtUtc = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            ModifiedAtUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            Attributes = (FileAttributes)reader.GetInt32(10),
            LastIndexedUtc = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            Status = (FileStatus)reader.GetInt32(12)
        };
    }
}

