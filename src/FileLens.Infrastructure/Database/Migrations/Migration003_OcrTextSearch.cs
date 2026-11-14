using Microsoft.Data.Sqlite;

namespace FileLens.Infrastructure.Database.Migrations;

public static class Migration003_OcrTextSearch
{
    public const int Version = 3;
    public const string Description = "Create OcrRecords table and indexes for Arabic and English OCR text";

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            -- 1. OcrRecords: stores granular OCR results, bounding boxes, confidence, and language
            CREATE TABLE IF NOT EXISTS OcrRecords (
                Id TEXT PRIMARY KEY,
                FileId TEXT NOT NULL,
                EngineName TEXT NOT NULL,
                ModelName TEXT NOT NULL,
                ModelVersion TEXT NOT NULL,
                Language TEXT NOT NULL,
                PageNumber INTEGER NULL,
                Confidence REAL NOT NULL,
                RawText TEXT NOT NULL,
                NormalizedText TEXT NOT NULL,
                RegionsJson TEXT NULL,
                ProcessingTimeMs INTEGER NOT NULL,
                ExtractedAtUtc TEXT NOT NULL,
                ErrorMessage TEXT NULL,
                FOREIGN KEY (FileId) REFERENCES IndexedFiles(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_OcrRecords_FileId ON OcrRecords(FileId);
            CREATE INDEX IF NOT EXISTS IX_OcrRecords_Confidence ON OcrRecords(Confidence);
            CREATE INDEX IF NOT EXISTS IX_OcrRecords_Language ON OcrRecords(Language);
        ";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

