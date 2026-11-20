using Microsoft.Data.Sqlite;

namespace FileLens.Infrastructure.Database.Migrations;

public static class Migration005_SemanticVisualEmbeddingMetadata
{
    public const int Version = 5;
    public const string Description = "Add provider, revision, and preprocessing metadata to visual embeddings";

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            ALTER TABLE VisualEmbeddings ADD COLUMN ProviderName TEXT NOT NULL DEFAULT 'FileLens-VisualFeature-CPU';
            ALTER TABLE VisualEmbeddings ADD COLUMN ModelRevision TEXT NOT NULL DEFAULT '';
            ALTER TABLE VisualEmbeddings ADD COLUMN Preprocessing TEXT NOT NULL DEFAULT '';
            CREATE INDEX IF NOT EXISTS IX_VisualEmbeddings_Model ON VisualEmbeddings(ProviderName, ModelName, ModelVersion, ModelRevision);";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
