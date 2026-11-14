# ADR-0002: Index and Metadata Storage

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens needs a local store for file metadata, extracted text chunks, visual asset records, job state and full-text search indexes. The store must work without a server process, survive process crashes gracefully, support hundreds of thousands of file records, and provide fast full-text search over filenames, paths and document text.

## Decision

Use **SQLite with FTS5** as the primary metadata and full-text search store.

- **Package:** `Microsoft.Data.Sqlite` (MIT license). This ships the `e_sqlite3` native library which includes FTS5 support out of the box — no separate native build required.
- **Schema design:**
  - Core tables: `SearchRoot`, `FileEntry`, `ExtractedDocument`, `TextChunk`, `VisualAsset`, `IndexJob`, `IndexError`, `SearchHistory`, `Favorite`.
  - FTS5 virtual tables for searchable filename/path and extracted text content.
  - Separate columns/tables for original text and normalized text (including Arabic-normalized variants). Original text is never overwritten by normalization.
  - All timestamps stored as UTC. Conversion to local time happens only in the UI layer.
  - Stable file IDs (GUIDs) independent of path; content hashes and file identity stored separately for reconciliation.
- **Connection management:** Single-writer with WAL mode for concurrent reads during indexing. Connection pooling via `Microsoft.Data.Sqlite`.
- **Migrations:** Schema versioned with a `SchemaVersion` table; forward-only migrations applied at startup.

## Alternatives considered

- **LiteDB:** Embedded NoSQL document store for .NET. Simpler object mapping, but no built-in full-text search comparable to FTS5. Smaller community and fewer production-scale references.
- **Entity Framework Core + SQLite:** EF Core adds an ORM layer with migrations support, but introduces abstraction overhead and complicates FTS5 usage (FTS5 virtual tables do not map cleanly to EF models). Direct ADO.NET via `Microsoft.Data.Sqlite` gives full control over FTS5 and custom SQL.
- **Lucene.NET:** Powerful full-text search, but adds a separate index alongside the metadata store, doubling storage management complexity. FTS5 keeps text search and metadata in one database.
- **PostgreSQL / SQL Server LocalDB:** Server-based databases add installation complexity and a background service. Violates the local-first, zero-server-dependency goal for a desktop app.

## Consequences

- **Positive:** Zero server dependency — the database is a single file in the user's app-data directory. Backup is a file copy.
- **Positive:** FTS5 provides proven full-text search with prefix queries, phrase matching and ranking (BM25) without an external search engine.
- **Positive:** `Microsoft.Data.Sqlite` is MIT-licensed and maintained by Microsoft, with FTS5 included in the bundled `e_sqlite3`.
- **Risk:** SQLite single-writer can become a bottleneck under heavy concurrent indexing. WAL mode and batched writes mitigate this; monitor write contention in Phase 1 benchmarks.
- **Risk:** FTS5 tokenizers may need customization for Arabic text (diacritics, tatweel, Alef/Ya normalization). Plan a custom tokenizer or normalized-text column strategy in Phase 3.
- **Follow-up:** Benchmark FTS5 query performance on a 100k-file corpus in Phase 1. Validate WAL mode behavior on network-mounted app-data directories (rare but possible).
