using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database.Migrations;
using FileLens.Infrastructure.Text;

namespace FileLens.Infrastructure.Database;

public sealed class SqliteDatabase : IIndexDatabase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteDatabase> _logger;
    private readonly ITextNormalizer _normalizer;

    public SqliteDatabase(
        SqliteConnectionFactory connectionFactory,
        ILogger<SqliteDatabase> logger,
        ITextNormalizer? normalizer = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _normalizer = normalizer ?? new TextNormalizer();
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

        if (currentVersion < Migration002_FullTextSearch.Version)
        {
            _logger.LogInformation("Applying Migration {Version}: {Description}", Migration002_FullTextSearch.Version, Migration002_FullTextSearch.Description);
            await Migration002_FullTextSearch.ApplyAsync(connection, transaction, cancellationToken);

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO SchemaMigrations (Version, Description, AppliedAtUtc) VALUES ($v, $desc, $applied);";
                cmd.Parameters.AddWithValue("$v", Migration002_FullTextSearch.Version);
                cmd.Parameters.AddWithValue("$desc", Migration002_FullTextSearch.Description);
                cmd.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (currentVersion < Migration003_OcrTextSearch.Version)
        {
            _logger.LogInformation("Applying Migration {Version}: {Description}", Migration003_OcrTextSearch.Version, Migration003_OcrTextSearch.Description);
            await Migration003_OcrTextSearch.ApplyAsync(connection, transaction, cancellationToken);

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO SchemaMigrations (Version, Description, AppliedAtUtc) VALUES ($v, $desc, $applied);";
                cmd.Parameters.AddWithValue("$v", Migration003_OcrTextSearch.Version);
                cmd.Parameters.AddWithValue("$desc", Migration003_OcrTextSearch.Description);
                cmd.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (currentVersion < Migration004_VisualSearch.Version)
        {
            _logger.LogInformation("Applying Migration {Version}: {Description}", Migration004_VisualSearch.Version, Migration004_VisualSearch.Description);
            await Migration004_VisualSearch.ApplyAsync(connection, transaction, cancellationToken);
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO SchemaMigrations (Version, Description, AppliedAtUtc) VALUES ($v, $desc, $applied);";
            cmd.Parameters.AddWithValue("$v", Migration004_VisualSearch.Version);
            cmd.Parameters.AddWithValue("$desc", Migration004_VisualSearch.Description);
            cmd.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (currentVersion < Migration005_SemanticVisualEmbeddingMetadata.Version)
        {
            _logger.LogInformation("Applying Migration {Version}: {Description}", Migration005_SemanticVisualEmbeddingMetadata.Version, Migration005_SemanticVisualEmbeddingMetadata.Description);
            await Migration005_SemanticVisualEmbeddingMetadata.ApplyAsync(connection, transaction, cancellationToken);
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO SchemaMigrations (Version, Description, AppliedAtUtc) VALUES ($v, $desc, $applied);";
            cmd.Parameters.AddWithValue("$v", Migration005_SemanticVisualEmbeddingMetadata.Version);
            cmd.Parameters.AddWithValue("$desc", Migration005_SemanticVisualEmbeddingMetadata.Description);
            cmd.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Database initialization complete. Current schema version: {Version}", Migration005_SemanticVisualEmbeddingMetadata.Version);
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
        await using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        // Clean FTS virtual tables first since SQLite virtual tables don't automatically follow external foreign keys
        cmd.CommandText = @"
            DELETE FROM DocumentTextFts WHERE FileId IN (SELECT Id FROM IndexedFiles WHERE RootId = $id);
            DELETE FROM DocumentChunkFts WHERE FileId IN (SELECT Id FROM IndexedFiles WHERE RootId = $id);
            DELETE FROM IndexRoots WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.Value.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

        // Ensure parent job exists to prevent foreign key constraint violation
        using (var ensureJobCmd = connection.CreateCommand())
        {
            ensureJobCmd.CommandText = @"
                INSERT OR IGNORE INTO IndexJobs (Id, RootId, JobType, Status, StartedAtUtc, CompletedAtUtc, ErrorMessage)
                VALUES ($jobId, $rootId, 1, 2, $started, $started, 'SystemJob');";
            ensureJobCmd.Parameters.AddWithValue("$jobId", error.JobId.ToString());
            ensureJobCmd.Parameters.AddWithValue("$rootId", error.RootId.Value.ToString());
            ensureJobCmd.Parameters.AddWithValue("$started", DateTime.UtcNow.ToString("o"));
            await ensureJobCmd.ExecuteNonQueryAsync(cancellationToken);
        }

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

    // =========================================================================
    // Phase 2: Full-Text Search and Document Extraction Operations
    // =========================================================================

    public async Task UpsertDocumentTextAsync(ExtractedDocument doc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doc);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        // 1. Fetch file name
        string fileName = string.Empty;
        using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT FileName FROM IndexedFiles WHERE Id = $fileId;";
            fetchCmd.Parameters.AddWithValue("$fileId", doc.FileId.ToString());
            var res = await fetchCmd.ExecuteScalarAsync(cancellationToken);
            if (res != null && res != DBNull.Value)
            {
                fileName = res.ToString() ?? string.Empty;
            }
        }

        // 2. Upsert DocumentText row
        string metadataJson = JsonSerializer.Serialize(doc.Metadata);
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO DocumentText (
                    FileId, ExtractorId, ExtractionVersion, Status, RawText, NormalizedText,
                    MetadataJson, ExtractedSizeBytes, ExtractedAtUtc, ErrorMessage
                )
                VALUES (
                    $fileId, $extractorId, $version, $status, $raw, $norm,
                    $meta, $size, $extractedAt, $err
                )
                ON CONFLICT(FileId) DO UPDATE SET
                    ExtractorId = excluded.ExtractorId,
                    ExtractionVersion = excluded.ExtractionVersion,
                    Status = excluded.Status,
                    RawText = excluded.RawText,
                    NormalizedText = excluded.NormalizedText,
                    MetadataJson = excluded.MetadataJson,
                    ExtractedSizeBytes = excluded.ExtractedSizeBytes,
                    ExtractedAtUtc = excluded.ExtractedAtUtc,
                    ErrorMessage = excluded.ErrorMessage;";

            cmd.Parameters.AddWithValue("$fileId", doc.FileId.ToString());
            cmd.Parameters.AddWithValue("$extractorId", doc.ExtractorId);
            cmd.Parameters.AddWithValue("$version", doc.ExtractionVersion);
            cmd.Parameters.AddWithValue("$status", (int)doc.Status);
            cmd.Parameters.AddWithValue("$raw", doc.RawText);
            cmd.Parameters.AddWithValue("$norm", doc.NormalizedText);
            cmd.Parameters.AddWithValue("$meta", metadataJson);
            cmd.Parameters.AddWithValue("$size", doc.ExtractedSizeBytes);
            cmd.Parameters.AddWithValue("$extractedAt", doc.ExtractedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$err", (object?)doc.ErrorMessage ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. Clear old chunks and FTS entries
        using (var cleanCmd = connection.CreateCommand())
        {
            cleanCmd.Transaction = transaction;
            cleanCmd.CommandText = @"
                DELETE FROM DocumentChunks WHERE FileId = $fileId;
                DELETE FROM DocumentTextFts WHERE FileId = $fileId;
                DELETE FROM DocumentChunkFts WHERE FileId = $fileId;";
            cleanCmd.Parameters.AddWithValue("$fileId", doc.FileId.ToString());
            await cleanCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 4. Populate DocumentTextFts and DocumentChunks if extracted
        if (doc.Status == ExtractionStatus.Extracted && !string.IsNullOrWhiteSpace(doc.RawText))
        {
            using (var ftsCmd = connection.CreateCommand())
            {
                ftsCmd.Transaction = transaction;
                ftsCmd.CommandText = @"
                    INSERT INTO DocumentTextFts (FileId, FileName, NormalizedText, RawText)
                    VALUES ($fileId, $fileName, $norm, $raw);";
                ftsCmd.Parameters.AddWithValue("$fileId", doc.FileId.ToString());
                ftsCmd.Parameters.AddWithValue("$fileName", fileName);
                ftsCmd.Parameters.AddWithValue("$norm", doc.NormalizedText);
                ftsCmd.Parameters.AddWithValue("$raw", doc.RawText);
                await ftsCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var chunksToInsert = doc.Chunks.Count > 0 ? doc.Chunks : new[]
            {
                new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    FileId = doc.FileId,
                    ChunkIndex = 0,
                    LocationType = "Document",
                    LocationReference = "Full Document",
                    RawText = doc.RawText,
                    NormalizedText = doc.NormalizedText
                }
            };

            foreach (var chunk in chunksToInsert)
            {
                using var chunkCmd = connection.CreateCommand();
                chunkCmd.Transaction = transaction;
                chunkCmd.CommandText = @"
                    INSERT INTO DocumentChunks (
                        Id, FileId, ChunkIndex, LocationType, LocationReference,
                        PageNumber, SheetName, CellAddress, SlideNumber, RawText, NormalizedText
                    )
                    VALUES (
                        $id, $fileId, $idx, $locType, $locRef,
                        $page, $sheet, $cell, $slide, $raw, $norm
                    );

                    INSERT INTO DocumentChunkFts (ChunkId, FileId, LocationReference, NormalizedText, RawText)
                    VALUES ($id, $fileId, $locRef, $norm, $raw);";

                chunkCmd.Parameters.AddWithValue("$id", chunk.Id.ToString());
                chunkCmd.Parameters.AddWithValue("$fileId", doc.FileId.ToString());
                chunkCmd.Parameters.AddWithValue("$idx", chunk.ChunkIndex);
                chunkCmd.Parameters.AddWithValue("$locType", chunk.LocationType);
                chunkCmd.Parameters.AddWithValue("$locRef", chunk.LocationReference);
                chunkCmd.Parameters.AddWithValue("$page", (object?)chunk.PageNumber ?? DBNull.Value);
                chunkCmd.Parameters.AddWithValue("$sheet", (object?)chunk.SheetName ?? DBNull.Value);
                chunkCmd.Parameters.AddWithValue("$cell", (object?)chunk.CellAddress ?? DBNull.Value);
                chunkCmd.Parameters.AddWithValue("$slide", (object?)chunk.SlideNumber ?? DBNull.Value);
                chunkCmd.Parameters.AddWithValue("$raw", chunk.RawText);
                chunkCmd.Parameters.AddWithValue("$norm", chunk.NormalizedText);

                await chunkCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ExtractedDocument?> GetExtractedDocumentByFileIdAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        ExtractedDocument? doc = null;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.FileId, f.FullPath, t.ExtractorId, t.ExtractionVersion, t.Status,
                       t.RawText, t.NormalizedText, t.MetadataJson, t.ExtractedSizeBytes,
                       t.ExtractedAtUtc, t.ErrorMessage
                FROM DocumentText t
                JOIN IndexedFiles f ON f.Id = t.FileId
                WHERE t.FileId = $fileId;";
            cmd.Parameters.AddWithValue("$fileId", fileId.ToString());

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var metadata = new Dictionary<string, string>();
                if (!reader.IsDBNull(7))
                {
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(7));
                        if (dict != null) metadata = dict;
                    }
                    catch { }
                }

                doc = new ExtractedDocument
                {
                    FileId = Guid.Parse(reader.GetString(0)),
                    FullPath = reader.GetString(1),
                    ExtractorId = reader.GetString(2),
                    ExtractionVersion = reader.GetInt32(3),
                    Status = (ExtractionStatus)reader.GetInt32(4),
                    RawText = reader.GetString(5),
                    NormalizedText = reader.GetString(6),
                    Metadata = metadata,
                    ExtractedSizeBytes = reader.GetInt64(8),
                    ExtractedAtUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
                    ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10)
                };
            }
        }

        if (doc == null) return null;

        var chunks = new List<DocumentChunk>();
        using (var chunkCmd = connection.CreateCommand())
        {
            chunkCmd.CommandText = @"
                SELECT Id, FileId, ChunkIndex, LocationType, LocationReference,
                       PageNumber, SheetName, CellAddress, SlideNumber, RawText, NormalizedText
                FROM DocumentChunks
                WHERE FileId = $fileId
                ORDER BY ChunkIndex ASC;";
            chunkCmd.Parameters.AddWithValue("$fileId", fileId.ToString());

            using var chunkReader = await chunkCmd.ExecuteReaderAsync(cancellationToken);
            while (await chunkReader.ReadAsync(cancellationToken))
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.Parse(chunkReader.GetString(0)),
                    FileId = Guid.Parse(chunkReader.GetString(1)),
                    ChunkIndex = chunkReader.GetInt32(2),
                    LocationType = chunkReader.GetString(3),
                    LocationReference = chunkReader.GetString(4),
                    PageNumber = chunkReader.IsDBNull(5) ? null : chunkReader.GetInt32(5),
                    SheetName = chunkReader.IsDBNull(6) ? null : chunkReader.GetString(6),
                    CellAddress = chunkReader.IsDBNull(7) ? null : chunkReader.GetString(7),
                    SlideNumber = chunkReader.IsDBNull(8) ? null : chunkReader.GetInt32(8),
                    RawText = chunkReader.GetString(9),
                    NormalizedText = chunkReader.GetString(10)
                });
            }
        }

        return new ExtractedDocument
        {
            FileId = doc.FileId,
            FullPath = doc.FullPath,
            ExtractorId = doc.ExtractorId,
            ExtractionVersion = doc.ExtractionVersion,
            Status = doc.Status,
            RawText = doc.RawText,
            NormalizedText = doc.NormalizedText,
            Chunks = chunks,
            Metadata = doc.Metadata,
            ExtractedSizeBytes = doc.ExtractedSizeBytes,
            ExtractedAtUtc = doc.ExtractedAtUtc,
            ErrorMessage = doc.ErrorMessage
        };
    }

    public async Task<SearchResultsPage> SearchFullTextAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sw = Stopwatch.StartNew();
        string normalizedQuery = _normalizer.NormalizeQuery(query.Text);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new SearchResultsPage
            {
                Items = Array.Empty<SearchResult>(),
                TotalMatches = 0,
                SearchDurationMs = sw.ElapsedMilliseconds,
                QueryUsed = string.Empty
            };
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var filterClauses = new List<string>();
        using var cmd = connection.CreateCommand();

        cmd.Parameters.AddWithValue("$ftsQuery", normalizedQuery);

        if (query.Extensions != null && query.Extensions.Count > 0)
        {
            var extParams = new List<string>();
            for (int i = 0; i < query.Extensions.Count; i++)
            {
                string pName = $"$ext{i}";
                extParams.Add(pName);
                cmd.Parameters.AddWithValue(pName, query.Extensions[i].ToLowerInvariant());
            }
            filterClauses.Add($"LOWER(f.Extension) IN ({string.Join(",", extParams)})");
        }

        if (query.RootIds != null && query.RootIds.Count > 0)
        {
            var rootParams = new List<string>();
            for (int i = 0; i < query.RootIds.Count; i++)
            {
                string pName = $"$root{i}";
                rootParams.Add(pName);
                cmd.Parameters.AddWithValue(pName, query.RootIds[i].Value.ToString());
            }
            filterClauses.Add($"f.RootId IN ({string.Join(",", rootParams)})");
        }

        if (query.FromDateUtc.HasValue)
        {
            filterClauses.Add("f.ModifiedAtUtc >= $fromDate");
            cmd.Parameters.AddWithValue("$fromDate", query.FromDateUtc.Value.ToString("o"));
        }

        if (query.ToDateUtc.HasValue)
        {
            filterClauses.Add("f.ModifiedAtUtc <= $toDate");
            cmd.Parameters.AddWithValue("$toDate", query.ToDateUtc.Value.ToString("o"));
        }

        if (query.MinSizeBytes.HasValue)
        {
            filterClauses.Add("f.SizeBytes >= $minSize");
            cmd.Parameters.AddWithValue("$minSize", query.MinSizeBytes.Value);
        }

        if (query.MaxSizeBytes.HasValue)
        {
            filterClauses.Add("f.SizeBytes <= $maxSize");
            cmd.Parameters.AddWithValue("$maxSize", query.MaxSizeBytes.Value);
        }

        string whereFilter = filterClauses.Count > 0 ? "AND " + string.Join(" AND ", filterClauses) : string.Empty;

        int limit = Math.Clamp(query.PageSize, 1, 500);
        int offset = Math.Max(0, (query.PageNumber - 1) * limit);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        cmd.CommandText = $@"
            WITH RankedMatches AS (
                SELECT
                    FileId,
                    ChunkId,
                    LocationReference,
                    snippet(DocumentChunkFts, 3, '<mark>', '</mark>', '...', 24) AS Snippet,
                    rank AS Score,
                    -- Chunk matches get priority 0, document matches get priority 1
                    0 AS MatchPriority
                FROM DocumentChunkFts
                WHERE DocumentChunkFts MATCH $ftsQuery

                UNION ALL

                SELECT
                    FileId,
                    NULL AS ChunkId,
                    'Document Title' AS LocationReference,
                    snippet(DocumentTextFts, 1, '<mark>', '</mark>', '...', 24) AS Snippet,
                    rank - 5.0 AS Score,
                    1 AS MatchPriority
                FROM DocumentTextFts
                WHERE DocumentTextFts MATCH $ftsQuery
            ),
            RankedWithRowNum AS (
                SELECT
                    FileId,
                    ChunkId,
                    LocationReference,
                    Snippet,
                    Score,
                    ROW_NUMBER() OVER (
                        PARTITION BY FileId
                        ORDER BY MatchPriority ASC, Score ASC
                    ) AS RowNum
                FROM RankedMatches
            ),
            BestMatches AS (
                SELECT
                    FileId,
                    ChunkId,
                    LocationReference,
                    Snippet,
                    Score AS BestScore
                FROM RankedWithRowNum
                WHERE RowNum = 1
            )
            SELECT
                b.FileId,
                f.RootId,
                f.FullPath,
                f.FileName,
                f.Extension,
                f.SizeBytes,
                f.ModifiedAtUtc,
                b.Snippet,
                b.BestScore,
                COALESCE(c.LocationReference, b.LocationReference) AS LocationRef,
                c.PageNumber,
                c.SheetName,
                c.CellAddress,
                c.SlideNumber,
                t.MetadataJson,
                COUNT(*) OVER() AS TotalCount
            FROM BestMatches b
            JOIN IndexedFiles f ON f.Id = b.FileId
            LEFT JOIN DocumentChunks c ON c.Id = b.ChunkId
            LEFT JOIN DocumentText t ON t.FileId = b.FileId
            WHERE 1=1 {whereFilter}
            ORDER BY b.BestScore ASC
            LIMIT $limit OFFSET $offset;";

        var results = new List<SearchResult>();
        int totalMatches = 0;

        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                totalMatches = reader.GetInt32(15);

                var metadata = new Dictionary<string, string>();
                if (!reader.IsDBNull(14))
                {
                    string json = reader.GetString(14);
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null) metadata = dict;
                    }
                    catch
                    {
                    }
                }

                string ext = reader.GetString(4);
                string? locRef = reader.IsDBNull(9) ? null : reader.GetString(9);
                bool isOcr = (!string.IsNullOrEmpty(locRef) && locRef.Contains("OCR", StringComparison.OrdinalIgnoreCase))
                    || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);

                results.Add(new SearchResult
                {
                    FileId = Guid.Parse(reader.GetString(0)),
                    RootId = new SearchRootId(Guid.Parse(reader.GetString(1))),
                    FullPath = reader.GetString(2),
                    FileName = reader.GetString(3),
                    Extension = ext,
                    SizeBytes = reader.GetInt64(5),
                    ModifiedAtUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                    Snippet = reader.GetString(7),
                    Score = reader.GetDouble(8),
                    LocationReference = locRef,
                    PageNumber = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    SheetName = reader.IsDBNull(11) ? null : reader.GetString(11),
                    CellAddress = reader.IsDBNull(12) ? null : reader.GetString(12),
                    SlideNumber = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    IsOcrMatch = isOcr,
                    DocumentMetadata = metadata
                });
            }
        }

        sw.Stop();
        return new SearchResultsPage
        {
            Items = results,
            TotalMatches = totalMatches,
            SearchDurationMs = sw.ElapsedMilliseconds,
            QueryUsed = normalizedQuery
        };
    }

    public async Task<ExtractionStatistics> GetExtractionStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM IndexedFiles) AS TotalFiles,
                (SELECT COUNT(*) FROM IndexedFiles f LEFT JOIN DocumentText t ON t.FileId = f.Id WHERE t.FileId IS NULL) AS AwaitingExtraction,
                (SELECT COUNT(*) FROM DocumentText WHERE Status = 2) AS SuccessfullyExtracted,
                (SELECT COUNT(*) FROM DocumentText WHERE Status IN (5, 6, 7)) AS FailedExtractions,
                (SELECT COUNT(*) FROM DocumentText WHERE Status = 3) AS UnsupportedFormats,
                (SELECT COALESCE(SUM(ExtractedSizeBytes), 0) FROM DocumentText) AS IndexedTextSizeBytes;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ExtractionStatistics
            {
                TotalFiles = reader.GetInt32(0),
                AwaitingExtraction = reader.GetInt32(1),
                SuccessfullyExtracted = reader.GetInt32(2),
                FailedExtractions = reader.GetInt32(3),
                UnsupportedFormats = reader.GetInt32(4),
                IndexedTextSizeBytes = reader.GetInt64(5)
            };
        }

        return new ExtractionStatistics();
    }

    public async Task<IReadOnlyList<IndexedFile>> GetFilesAwaitingExtractionAsync(
        SearchRootId? rootId = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        string rootFilter = rootId.HasValue ? "AND f.RootId = $rootId" : string.Empty;
        if (rootId.HasValue)
        {
            cmd.Parameters.AddWithValue("$rootId", rootId.Value.Value.ToString());
        }

        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        cmd.CommandText = $@"
            SELECT f.Id, f.RootId, f.FullPath, f.NormalizedPath, f.RelativePath, f.FileName,
                   f.Extension, f.SizeBytes, f.CreatedAtUtc, f.ModifiedAtUtc, f.Attributes,
                   f.LastIndexedUtc, f.Status
            FROM IndexedFiles f
            LEFT JOIN DocumentText t ON t.FileId = f.Id
            WHERE f.Status = 1
              AND (t.FileId IS NULL OR f.ModifiedAtUtc > t.ExtractedAtUtc)
              {rootFilter}
            ORDER BY f.LastIndexedUtc ASC
            LIMIT $limit;";

        var files = new List<IndexedFile>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(ReadIndexedFile(reader));
        }

        return files;
    }

    // ==========================================
    // Phase 3: OCR Storage and Retrieval
    // ==========================================

    public async Task UpsertOcrResultAsync(OcrRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        // 1. Insert or replace in OcrRecords
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO OcrRecords (
                    Id, FileId, EngineName, ModelName, ModelVersion, Language,
                    PageNumber, Confidence, RawText, NormalizedText, RegionsJson,
                    ProcessingTimeMs, ExtractedAtUtc, ErrorMessage
                ) VALUES (
                    $id, $fileId, $engine, $model, $ver, $lang,
                    $page, $conf, $raw, $norm, $regions,
                    $timeMs, $extractedAt, $err
                )
                ON CONFLICT(Id) DO UPDATE SET
                    EngineName = excluded.EngineName,
                    ModelName = excluded.ModelName,
                    ModelVersion = excluded.ModelVersion,
                    Language = excluded.Language,
                    PageNumber = excluded.PageNumber,
                    Confidence = excluded.Confidence,
                    RawText = excluded.RawText,
                    NormalizedText = excluded.NormalizedText,
                    RegionsJson = excluded.RegionsJson,
                    ProcessingTimeMs = excluded.ProcessingTimeMs,
                    ExtractedAtUtc = excluded.ExtractedAtUtc,
                    ErrorMessage = excluded.ErrorMessage;";

            cmd.Parameters.AddWithValue("$id", record.Id.ToString());
            cmd.Parameters.AddWithValue("$fileId", record.FileId.ToString());
            cmd.Parameters.AddWithValue("$engine", record.EngineName);
            cmd.Parameters.AddWithValue("$model", record.ModelName);
            cmd.Parameters.AddWithValue("$ver", record.ModelVersion);
            cmd.Parameters.AddWithValue("$lang", record.Language);
            cmd.Parameters.AddWithValue("$page", (object?)record.PageNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$conf", record.Confidence);
            cmd.Parameters.AddWithValue("$raw", record.RawText);
            cmd.Parameters.AddWithValue("$norm", record.NormalizedText);
            cmd.Parameters.AddWithValue("$regions", (object?)record.RegionsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$timeMs", record.ProcessingTimeMs);
            cmd.Parameters.AddWithValue("$extractedAt", record.ExtractedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$err", (object?)record.ErrorMessage ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Also register into DocumentChunks so FTS5 immediately indexes it
        string chunkLoc = record.PageNumber.HasValue ? $"Page {record.PageNumber.Value} (OCR)" : "Image (OCR)";
        Guid chunkId = Guid.NewGuid();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO DocumentChunks (
                    Id, FileId, ChunkIndex, LocationType, LocationReference,
                    PageNumber, SheetName, CellAddress, SlideNumber,
                    RawText, NormalizedText
                ) VALUES (
                    $id, $fileId, $chunkIdx, 'Ocr', $locRef,
                    $page, NULL, NULL, NULL,
                    $raw, $norm
                );";

            cmd.Parameters.AddWithValue("$id", chunkId.ToString());
            cmd.Parameters.AddWithValue("$fileId", record.FileId.ToString());
            cmd.Parameters.AddWithValue("$chunkIdx", record.PageNumber ?? 0);
            cmd.Parameters.AddWithValue("$locRef", chunkLoc);
            cmd.Parameters.AddWithValue("$page", (object?)record.PageNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$raw", record.RawText);
            cmd.Parameters.AddWithValue("$norm", record.NormalizedText);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. Insert into DocumentChunkFts
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO DocumentChunkFts (ChunkId, FileId, LocationReference, NormalizedText, RawText)
                VALUES ($chunkId, $fileId, $locRef, $norm, $raw);";

            cmd.Parameters.AddWithValue("$chunkId", chunkId.ToString());
            cmd.Parameters.AddWithValue("$fileId", record.FileId.ToString());
            cmd.Parameters.AddWithValue("$locRef", chunkLoc);
            cmd.Parameters.AddWithValue("$norm", record.NormalizedText);
            cmd.Parameters.AddWithValue("$raw", record.RawText);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OcrRecord>> GetOcrResultsByFileIdAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, FileId, EngineName, ModelName, ModelVersion, Language,
                   PageNumber, Confidence, RawText, NormalizedText, RegionsJson,
                   ProcessingTimeMs, ExtractedAtUtc, ErrorMessage
            FROM OcrRecords
            WHERE FileId = $fileId
            ORDER BY PageNumber ASC, ExtractedAtUtc ASC;";
        cmd.Parameters.AddWithValue("$fileId", fileId.ToString());

        var list = new List<OcrRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new OcrRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                FileId = Guid.Parse(reader.GetString(1)),
                EngineName = reader.GetString(2),
                ModelName = reader.GetString(3),
                ModelVersion = reader.GetString(4),
                Language = reader.GetString(5),
                PageNumber = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Confidence = reader.GetDouble(7),
                RawText = reader.GetString(8),
                NormalizedText = reader.GetString(9),
                RegionsJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                ProcessingTimeMs = reader.GetInt32(11),
                ExtractedAtUtc = DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
                ErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        }
        return list;
    }

    public async Task<OcrStatistics> GetOcrStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM IndexedFiles f WHERE LOWER(f.Extension) IN ('.jpg', '.jpeg', '.png', '.tiff', '.bmp', '.webp', '.gif')) AS TotalQueued,
                (SELECT COUNT(*) FROM OcrRecords WHERE ErrorMessage IS NULL) AS SuccessfullyProcessed,
                (SELECT COUNT(*) FROM OcrRecords WHERE ErrorMessage IS NOT NULL) AS Failed,
                (SELECT COALESCE(AVG(Confidence), 0.0) FROM OcrRecords WHERE ErrorMessage IS NULL) AS AverageConfidence,
                (SELECT COALESCE(SUM(LENGTH(RawText)), 0) FROM OcrRecords) AS TotalCharacters;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new OcrStatistics
            {
                TotalQueued = reader.GetInt32(0),
                SuccessfullyProcessed = reader.GetInt32(1),
                Failed = reader.GetInt32(2),
                AverageConfidence = reader.GetDouble(3),
                TotalOcrCharacters = reader.GetInt64(4)
            };
        }
        return new OcrStatistics();
    }

    public async Task<IReadOnlyList<IndexedFile>> GetFilesAwaitingOcrAsync(int limit = 500, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        cmd.CommandText = @"
            SELECT f.Id, f.RootId, f.FullPath, f.NormalizedPath, f.RelativePath, f.FileName,
                   f.Extension, f.SizeBytes, f.CreatedAtUtc, f.ModifiedAtUtc, f.Attributes,
                   f.LastIndexedUtc, f.Status
            FROM IndexedFiles f
            LEFT JOIN OcrRecords o ON o.FileId = f.Id
            WHERE f.Status = 1
              AND LOWER(f.Extension) IN ('.jpg', '.jpeg', '.png', '.tiff', '.bmp', '.webp', '.gif')
              AND (o.FileId IS NULL OR f.ModifiedAtUtc > o.ExtractedAtUtc)
            ORDER BY f.LastIndexedUtc ASC
            LIMIT $limit;";

        var files = new List<IndexedFile>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(ReadIndexedFile(reader));
        }
        return files;
    }

    public async Task UpsertVisualAssetAsync(VisualAsset asset, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO VisualAssets
            (Id,FileId,FullPath,Extension,Width,Height,SizeBytes,ContentHash,PerceptualHash,IndexedAtUtc,Status)
            VALUES ($id,$file,$path,$ext,$w,$h,$size,$hash,$phash,$at,$status)
            ON CONFLICT(FileId) DO UPDATE SET Id=excluded.Id,FullPath=excluded.FullPath,Extension=excluded.Extension,
            Width=excluded.Width,Height=excluded.Height,SizeBytes=excluded.SizeBytes,ContentHash=excluded.ContentHash,
            PerceptualHash=excluded.PerceptualHash,IndexedAtUtc=excluded.IndexedAtUtc,Status=excluded.Status;";
        cmd.Parameters.AddWithValue("$id", asset.Id.ToString());
        cmd.Parameters.AddWithValue("$file", asset.FileId.ToString());
        cmd.Parameters.AddWithValue("$path", asset.FullPath);
        cmd.Parameters.AddWithValue("$ext", asset.Extension);
        cmd.Parameters.AddWithValue("$w", asset.Width);
        cmd.Parameters.AddWithValue("$h", asset.Height);
        cmd.Parameters.AddWithValue("$size", asset.SizeBytes);
        cmd.Parameters.AddWithValue("$hash", asset.ContentHash);
        cmd.Parameters.AddWithValue("$phash", asset.PerceptualHash);
        cmd.Parameters.AddWithValue("$at", asset.IndexedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$status", asset.Status);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertVisualEmbeddingAsync(VisualEmbedding embedding, CancellationToken cancellationToken = default)
    {
        var bytes = new byte[embedding.Vector.Length * sizeof(float)];
        Buffer.BlockCopy(embedding.Vector, 0, bytes, 0, bytes.Length);
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO VisualEmbeddings (AssetId,ModelName,ModelVersion,Dimension,Vector,CreatedAtUtc,ProviderName,ModelRevision,Preprocessing)
            VALUES ($id,$name,$version,$dimension,$vector,$created,$provider,$revision,$preprocessing)
            ON CONFLICT(AssetId) DO UPDATE SET ModelName=excluded.ModelName,ModelVersion=excluded.ModelVersion,
            Dimension=excluded.Dimension,Vector=excluded.Vector,CreatedAtUtc=excluded.CreatedAtUtc,
            ProviderName=excluded.ProviderName,ModelRevision=excluded.ModelRevision,Preprocessing=excluded.Preprocessing;";
        cmd.Parameters.AddWithValue("$id", embedding.AssetId.ToString());
        cmd.Parameters.AddWithValue("$name", embedding.ModelName);
        cmd.Parameters.AddWithValue("$version", embedding.ModelVersion);
        cmd.Parameters.AddWithValue("$dimension", embedding.Vector.Length);
        cmd.Parameters.Add("$vector", SqliteType.Blob).Value = bytes;
        cmd.Parameters.AddWithValue("$created", embedding.CreatedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$provider", embedding.ProviderName);
        cmd.Parameters.AddWithValue("$revision", embedding.ModelRevision);
        cmd.Parameters.AddWithValue("$preprocessing", embedding.Preprocessing);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(VisualAsset Asset, VisualEmbedding Embedding)>> GetVisualEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT a.Id,a.FileId,a.FullPath,a.Extension,a.Width,a.Height,a.SizeBytes,a.ContentHash,
            a.PerceptualHash,a.IndexedAtUtc,a.Status,e.ModelName,e.ModelVersion,e.Vector,e.CreatedAtUtc,e.ProviderName,e.ModelRevision,e.Preprocessing,e.Dimension
            FROM VisualAssets a INNER JOIN VisualEmbeddings e ON e.AssetId=a.Id
            INNER JOIN IndexedFiles f ON f.Id=a.FileId WHERE f.Status IN (0,1);";
        var result = new List<(VisualAsset, VisualEmbedding)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var asset = new VisualAsset(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6), reader.GetString(7), reader.GetString(8), DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind), reader.GetString(10));
            var bytes = (byte[])reader[13];
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            var embedding = new VisualEmbedding(asset.Id, reader.GetString(11), reader.GetString(12), vector, DateTime.Parse(reader.GetString(14), null, DateTimeStyles.RoundtripKind), reader.GetString(15), reader.GetString(16), reader.GetString(17));
            result.Add((asset, embedding));
        }
        return result;
    }

    public async Task RemoveVisualAssetAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM VisualAssets WHERE FileId = $file;";
        cmd.Parameters.AddWithValue("$file", fileId.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

