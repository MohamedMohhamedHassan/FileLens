using Microsoft.Data.Sqlite;

namespace FileLens.Infrastructure.Database.Migrations;

public static class Migration002_FullTextSearch
{
    public const int Version = 2;
    public const string Description = "Create DocumentText, DocumentChunks, and SQLite FTS5 Full-Text Search Virtual Tables";

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            -- 1. DocumentText: stores extracted body text and metadata per file
            CREATE TABLE IF NOT EXISTS DocumentText (
                FileId TEXT PRIMARY KEY,
                ExtractorId TEXT NOT NULL,
                ExtractionVersion INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                RawText TEXT NOT NULL,
                NormalizedText TEXT NOT NULL,
                MetadataJson TEXT NULL,
                ExtractedSizeBytes INTEGER NOT NULL,
                ExtractedAtUtc TEXT NOT NULL,
                ErrorMessage TEXT NULL,
                FOREIGN KEY (FileId) REFERENCES IndexedFiles(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_DocumentText_Status ON DocumentText(Status);

            -- 2. DocumentChunks: granular structural segments (pages, sheets, slides)
            CREATE TABLE IF NOT EXISTS DocumentChunks (
                Id TEXT PRIMARY KEY,
                FileId TEXT NOT NULL,
                ChunkIndex INTEGER NOT NULL,
                LocationType TEXT NOT NULL,
                LocationReference TEXT NOT NULL,
                PageNumber INTEGER NULL,
                SheetName TEXT NULL,
                CellAddress TEXT NULL,
                SlideNumber INTEGER NULL,
                RawText TEXT NOT NULL,
                NormalizedText TEXT NOT NULL,
                FOREIGN KEY (FileId) REFERENCES IndexedFiles(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_DocumentChunks_FileId ON DocumentChunks(FileId);

            -- 3. DocumentTextFts: FTS5 virtual table for document body and filename search
            CREATE VIRTUAL TABLE IF NOT EXISTS DocumentTextFts USING fts5(
                FileId UNINDEXED,
                FileName,
                NormalizedText,
                RawText UNINDEXED,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            -- 4. DocumentChunkFts: FTS5 virtual table for locating exact matching pages/slides/sheets
            CREATE VIRTUAL TABLE IF NOT EXISTS DocumentChunkFts USING fts5(
                ChunkId UNINDEXED,
                FileId UNINDEXED,
                LocationReference,
                NormalizedText,
                RawText UNINDEXED,
                tokenize = 'unicode61 remove_diacritics 2'
            );
        ";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

