# ADR-0007: Preview and Thumbnail Cache

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens displays thumbnails and previews for search results. Generated preview data (thumbnails, rendered pages, extracted frames) must be stored somewhere accessible to the UI. This cache must never write into source file directories, must respect user privacy, and must be manageable (clearable, size-bounded).

## Decision

Store all preview and thumbnail data in a **per-user application-data directory**, separate from source files and the main index database.

- **Cache location:** `%LOCALAPPDATA%\FileLens\cache\` (e.g., `C:\Users\<user>\AppData\Local\FileLens\cache\`). This follows Windows conventions for per-user, machine-local application data.
- **Interface:** `IPreviewProvider` generates and retrieves previews/thumbnails for a given file entry and requested dimensions.
- **Cache key:** `SHA256(contentHash + dimensions + previewType)` — ensures cache hits are content-addressed and dimension-specific. A file with changed content gets a new preview automatically.
- **Size management:**
  - Configurable maximum cache size (default TBD, e.g., 2 GB).
  - LRU eviction when the cache exceeds the configured limit.
  - User-accessible "Clear cache" control in Settings.
  - Cache is fully rebuildable from source files — clearing it loses only generation time, not data.
- **Privacy:**
  - No source file content, paths or filenames appear in cache filenames (content-hash keys only).
  - No source content written to log files by default (see ADR-0008).
  - Cache directory inherits the user's Windows ACLs — no special permissions needed.
- **Safety:**
  - Generated previews are rendered to safe bitmap formats (PNG, JPEG). No executable content in the cache.
  - Preview generation enforces timeouts and memory limits per file.
  - If preview generation fails, the UI shows a "preview unavailable" placeholder — never a raw error dump.

## Alternatives considered

- **Store thumbnails in the SQLite database:** Simpler single-file storage, but BLOBs inflate the database size, slow down backup/migration and complicate cache eviction. File-based cache with hash keys is more efficient for binary data.
- **Write previews alongside source files:** Violates the core safety rule — never write generated data into source directories. Also fails for read-only media and network shares.
- **Use Windows thumbnail cache (thumbs.db / Explorer shell):** Uncontrollable by the application, inconsistent quality, no support for custom preview types (PDF page renders, slide thumbnails, PSD layers).
- **%APPDATA% (Roaming) instead of %LOCALAPPDATA%:** Roaming app data syncs across domain-joined machines, which would upload cache data to a server. Local app data stays on the machine, consistent with local-first principles.

## Consequences

- **Positive:** Clean separation between source files, index database and generated cache. Each can be managed independently.
- **Positive:** Content-hash keys enable automatic cache invalidation when files change, without explicit invalidation logic.
- **Positive:** User has full control: clear, resize or relocate the cache from Settings.
- **Risk:** Cache size can grow large for users with many high-resolution images or documents. The configurable limit and LRU eviction mitigate this, but the default must be chosen carefully.
- **Risk:** Cache rebuild after clearing takes time proportional to the number of indexed files. Consider lazy regeneration (on-demand when a preview is viewed) rather than eager rebuild.
- **Follow-up:** Determine default cache size limit based on Phase 2 benchmarks with representative file sets. Evaluate whether the cache directory should be relocatable via Settings (e.g., to a faster drive).
