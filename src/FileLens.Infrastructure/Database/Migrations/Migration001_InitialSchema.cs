using Microsoft.Data.Sqlite;

namespace FileLens.Infrastructure.Database.Migrations;

public static class Migration001_InitialSchema
{
    public const int Version = 1;
    public const string Description = "Create Initial Schema (IndexRoots, IndexedFiles, IndexJobs, IndexJobProgress, IndexErrors)";

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            -- 1. IndexRoots
            CREATE TABLE IF NOT EXISTS IndexRoots (
                Id TEXT PRIMARY KEY,
                Path TEXT NOT NULL UNIQUE,
                NormalizedPath TEXT NOT NULL UNIQUE,
                RootType INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 1,
                LastScanUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_IndexRoots_NormalizedPath ON IndexRoots(NormalizedPath);
            CREATE INDEX IF NOT EXISTS IX_IndexRoots_IsEnabled ON IndexRoots(IsEnabled);

            -- 2. IndexedFiles
            CREATE TABLE IF NOT EXISTS IndexedFiles (
                Id TEXT PRIMARY KEY,
                RootId TEXT NOT NULL,
                FullPath TEXT NOT NULL UNIQUE,
                NormalizedPath TEXT NOT NULL UNIQUE,
                RelativePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                Extension TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                ModifiedAtUtc TEXT NOT NULL,
                Attributes INTEGER NOT NULL,
                LastIndexedUtc TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (RootId) REFERENCES IndexRoots(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_IndexedFiles_RootId_Status ON IndexedFiles(RootId, Status);
            CREATE INDEX IF NOT EXISTS IX_IndexedFiles_NormalizedPath ON IndexedFiles(NormalizedPath);
            CREATE INDEX IF NOT EXISTS IX_IndexedFiles_FileName ON IndexedFiles(FileName);
            CREATE INDEX IF NOT EXISTS IX_IndexedFiles_Extension ON IndexedFiles(Extension);

            -- 3. IndexJobs
            CREATE TABLE IF NOT EXISTS IndexJobs (
                Id TEXT PRIMARY KEY,
                RootId TEXT NOT NULL,
                JobType INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 1,
                StartedAtUtc TEXT NULL,
                CompletedAtUtc TEXT NULL,
                ErrorMessage TEXT NULL,
                FOREIGN KEY (RootId) REFERENCES IndexRoots(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_IndexJobs_RootId_Status ON IndexJobs(RootId, Status);

            -- 4. IndexJobProgress
            CREATE TABLE IF NOT EXISTS IndexJobProgress (
                JobId TEXT PRIMARY KEY,
                RootId TEXT NOT NULL,
                CurrentDirectory TEXT NULL,
                CurrentFile TEXT NULL,
                DiscoveredCount INTEGER NOT NULL DEFAULT 0,
                ProcessedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0,
                FailedCount INTEGER NOT NULL DEFAULT 0,
                LastUpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (JobId) REFERENCES IndexJobs(Id) ON DELETE CASCADE
            );

            -- 5. IndexErrors
            CREATE TABLE IF NOT EXISTS IndexErrors (
                Id TEXT PRIMARY KEY,
                JobId TEXT NOT NULL,
                RootId TEXT NOT NULL,
                FilePath TEXT NULL,
                ErrorCategory TEXT NOT NULL,
                Message TEXT NOT NULL,
                LoggedAtUtc TEXT NOT NULL,
                FOREIGN KEY (JobId) REFERENCES IndexJobs(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_IndexErrors_JobId ON IndexErrors(JobId);
            CREATE INDEX IF NOT EXISTS IX_IndexErrors_RootId ON IndexErrors(RootId);
        ";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

