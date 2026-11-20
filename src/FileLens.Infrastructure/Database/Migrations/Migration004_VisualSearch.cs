using Microsoft.Data.Sqlite;

namespace FileLens.Infrastructure.Database.Migrations;

public static class Migration004_VisualSearch
{
    public const int Version = 4;
    public const string Description = "Create visual assets, local embeddings, and visual indexing job tables";

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS VisualAssets (
                Id TEXT PRIMARY KEY, FileId TEXT NOT NULL UNIQUE, FullPath TEXT NOT NULL,
                Extension TEXT NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL,
                SizeBytes INTEGER NOT NULL, ContentHash TEXT NOT NULL, PerceptualHash TEXT NOT NULL,
                IndexedAtUtc TEXT NOT NULL, Status TEXT NOT NULL,
                FOREIGN KEY (FileId) REFERENCES IndexedFiles(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_VisualAssets_ContentHash ON VisualAssets(ContentHash);
            CREATE INDEX IF NOT EXISTS IX_VisualAssets_PerceptualHash ON VisualAssets(PerceptualHash);
            CREATE TABLE IF NOT EXISTS VisualEmbeddings (
                AssetId TEXT PRIMARY KEY, ModelName TEXT NOT NULL, ModelVersion TEXT NOT NULL,
                Dimension INTEGER NOT NULL, Vector BLOB NOT NULL, CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (AssetId) REFERENCES VisualAssets(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS VisualIndexJobs (
                Id TEXT PRIMARY KEY, RootId TEXT NULL, Status TEXT NOT NULL,
                ProcessedCount INTEGER NOT NULL DEFAULT 0, TotalCount INTEGER NOT NULL DEFAULT 0,
                StartedAtUtc TEXT NOT NULL, CompletedAtUtc TEXT NULL, ErrorMessage TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS VisualIndexErrors (
                Id TEXT PRIMARY KEY, JobId TEXT NOT NULL, FileId TEXT NULL,
                ErrorCategory TEXT NOT NULL, Message TEXT NOT NULL, LoggedAtUtc TEXT NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
