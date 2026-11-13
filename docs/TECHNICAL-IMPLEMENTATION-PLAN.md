# FileLens — Technical Implementation Plan

**Product type:** Windows desktop application  
**Primary use:** Search a user's selected local disks, folders, external drives and authenticated network shares by filename, document text, OCR text, image content and selected visual regions.  
**Delivery approach:** Local-first modular monolith with a separate, narrowly scoped AI worker for computer-vision/OCR dependencies.

## 1. Product boundaries and realistic expectations

The product must discover files with any extension, index their names, paths, sizes, dates and available metadata, and show them in results. Full-text or visual understanding is format-dependent: each format needs a parser, renderer or extractor. Proprietary and unknown binary formats must not be represented as fully searchable until an adapter exists.

The app must never modify source files. It must index only roots explicitly selected by the user and respect the current Windows account's permissions. Network shares use the user's authorized SMB access; the application does not bypass ACLs.

The first release is a single-user Windows 11 desktop application. Design interfaces so a later multi-user/server edition can be considered, but do not build server-scale features in the MVP.

## 2. Recommended technical stack

| Area | Recommended starting point | Reason / constraint |
|---|---|---|
| Desktop UI | .NET 10 LTS + WPF + MVVM | Windows-first, mature desktop and filesystem integration. .NET 10 is LTS through November 2028; confirm runtime availability on target PCs. |
| Core/application | C# .NET 10 class libraries; dependency injection | Keeps domain/indexing/search logic testable and independent of the UI. |
| Local service boundary | In-process service interfaces first; optional loopback ASP.NET Core API only if a separate UI/process needs it | Avoid unnecessary local HTTP services in the MVP. If enabled, bind only to loopback and authenticate local calls. |
| Metadata and text index | SQLite with FTS5 | Simple local persistence and full-text search. Validate FTS5 availability in the selected native SQLite package. |
| Vector search | `IVectorIndex` abstraction; start with HNSWLib/hnswlib or a locally hosted vector engine after benchmark | Keeps approximate-nearest-neighbor implementation replaceable. Store vector IDs and source mappings in SQLite. |
| AI worker | Python 3.12+ isolated environment, local process over localhost/stdio/pipe | Python ecosystem is practical for OCR and computer vision. Keep IPC versioned and tightly scoped. |
| Image similarity | CLIP/OpenCLIP-family image embeddings, subject to model license and offline tests | General semantic visual similarity for photos, objects, products, logos and scenes. Not a guarantee of exact identity matching. |
| Region/object detection | OpenCV plus a selected object-detection model | Draw/select a crop first; optional detection can propose boxes. Choose a model only after speed/accuracy/license evaluation. |
| OCR | `IOcrEngine` abstraction; benchmark PaddleOCR and Tesseract with Arabic + English test corpus | Accuracy varies by scan quality, fonts, layout and model. Keep per-language configuration and confidence values. |
| Video | FFmpeg-based frame extraction and metadata, subject to the selected build's licensing | Sample frames by interval and scene change, not every frame by default. Store timestamps. |
| Documents | Format-specific adapters; evaluate Apache Tika as an optional fallback | Parser selection must account for security, maintenance, licensing and supported platforms. |
| Tests | xUnit for .NET; pytest for Python; integration fixtures; performance benchmarks | Tests must cover corrupt files, denied access, cancellation, large files and malformed archives. |
| Packaging | Signed Windows installer/MSIX or self-contained installer after MVP | Bundle or install the Python worker and models with version checks; document model and FFmpeg licenses. |

**Important version note:** Use .NET 10 LTS as the stable default rather than targeting a preview SDK. Microsoft lists .NET 10 as supported through November 2028. Confirm the supported Windows version and Desktop Runtime on every deployment target.

## 3. Architecture

```text
┌──────────────────────────────────────────────────────────────┐
│                    WPF Desktop Application                    │
│ Dashboard | Text Search | Visual Search | Preview | Settings  │
└──────────────────────────────┬───────────────────────────────┘
                               │ application interfaces / DTOs
┌──────────────────────────────▼───────────────────────────────┐
│                    Application / Domain                       │
│ Search orchestration | Index jobs | Root management | ACL     │
│ Result ranking | Preview navigation | Configuration          │
└──────────────┬─────────────────────┬──────────────────────────┘
               │                     │
┌──────────────▼─────────────┐  ┌────▼─────────────────────────┐
│ Indexing and extraction    │  │ Search and storage            │
│ Enumeration / watcher      │  │ SQLite metadata + FTS5        │
│ Format adapters            │  │ Vector index abstraction      │
│ OCR / preview / assets     │  │ Search filters / ranking      │
└──────────────┬─────────────┘  └────▲─────────────────────────┘
               │                    │
        versioned local IPC          │ vectors and extraction DTOs
               │                    │
┌──────────────▼────────────────────┴──────────────────────────┐
│                 Python AI Worker (local only)                │
│ OCR | image embeddings | optional detection | frame analysis  │
└──────────────────────────────────────────────────────────────┘

Sources: user-approved local roots, removable drives and SMB/UNC shares.
Cache/index: application data directory; never write generated data into source roots.
```

Start with a modular monolith. The Python worker is a process boundary for native AI dependencies, not a general-purpose backend. If process communication adds too much complexity at the beginning, implement a local adapter interface and run it in-process during the prototype, then isolate it after measuring deployment/runtime conflicts.

### Core interfaces

Define contracts before adding implementations:

- `IFileEnumerator`: enumerate one approved root; honor cancellation and exclusions.
- `IFileClassifier`: detect actual file type using extension plus safe signature sniffing.
- `IFileMetadataExtractor`: basic metadata for any file.
- `ITextExtractor`: extract text and structured chunks from supported formats.
- `IVisualAssetExtractor`: extract embedded images, page renders, slides, layers and video frames.
- `IPreviewProvider`: create/serve a safe preview or thumbnail.
- `IOcrEngine`: OCR an image/page with language and layout options.
- `IImageEmbeddingProvider`: create embeddings for full images and selected crops.
- `IVectorIndex`: add, remove, update and query vectors.
- `IIndexJobQueue`: schedule work with bounded concurrency, retries and cancellation.
- `IFileAccessPolicy`: verify the user can read a source before extraction/preview.

Each adapter reports capabilities, supported extensions, maximum safe input size, timeout, and extraction status.

## 4. Data model

Use SQLite as the source of truth for indexed metadata. Suggested entities:

- `SearchRoot`: root path, root type (local/UNC/removable), enabled state, include/exclude rules, last successful scan.
- `FileEntry`: stable ID, canonical path, normalized path key, extension, detected MIME/type, size, created/modified UTC, file identity if available, content hash, status, last indexed UTC.
- `ExtractedDocument`: file ID, extractor ID/version, original text location, normalized text reference, extraction status and diagnostics.
- `TextChunk`: file ID, chunk ID, text, normalized text, page/slide/sheet/paragraph locator, character offsets.
- `VisualAsset`: parent file ID, asset type, thumbnail/cache key, width/height, page/slide/layer/frame timestamp, bounding box, content hash, embedding ID.
- `IndexJob`: root/file, job type, queue status, progress, attempt count, cancellation and timestamps.
- `IndexError`: file/root, phase, error category, safe diagnostic, retryability.
- `SearchHistory` and `Favorite`: user-facing convenience data.

Use SQLite FTS5 for searchable names, paths and extracted text. Keep source text, normalized text and Arabic-normalized text distinct. Never put raw document bodies into logs.

## 5. Indexing pipeline

1. User adds a root and confirms the scope and exclusions.
2. Validate that the root exists and is readable. Record the user's access context; do not request elevation automatically.
3. Enumerate files recursively with bounded concurrency. Skip configured system folders, recycle bins, application caches and excluded paths. Avoid symlink/junction traversal by default.
4. Upsert basic metadata for every discovered file regardless of extension.
5. Detect actual format. Route known formats to adapters; unknown types remain `MetadataOnly` with a user-visible reason.
6. Compare size, modified time and available file identity/hash to determine whether expensive extraction must run again.
7. Extract document text and chunks; extract embedded images/pages/slides/layers/video frames where supported.
8. Run OCR only on images/scanned pages where needed. Preserve OCR language, confidence and source position.
9. Generate visual embeddings in bounded batches. Apply CPU/GPU resource limits and backpressure.
10. Commit metadata, FTS rows, visual-asset mappings and vector IDs consistently. On a failure, retain the file record and mark the failed capability; do not lose the whole root scan.
11. Show progress, counts, skipped files, access-denied items and retryable errors.
12. Run incremental reconciliation. Use `FileSystemWatcher` only as a change hint; handle buffer overflow and network-share limitations with periodic reconciliation scans. For very large NTFS volumes, evaluate the USN Change Journal as a later optimization, not an MVP dependency.

All scanning and parsing must be cancellable. Large-file limits, archive expansion limits, page/frame limits, timeouts and per-root concurrency must be configurable.

## 6. Search design

### Text search

- Exact phrase, all terms, any terms, exclusions, prefix and fuzzy matching.
- Search name, path, extracted text, OCR text and metadata separately or together.
- Filters: extension/type, root, folder, date range, size range, file status, source type.
- Arabic + English: normalize Unicode consistently, optionally strip diacritics/tatweel for a secondary search field, normalize common Alef/Ya variants without replacing the original stored text. Add regression tests with real Arabic samples.
- Return matching snippet/highlight and the location (page, sheet, slide or paragraph) where possible.
- Keep ranking explainable: filename hit, exact phrase hit, body hit, OCR hit and fuzzy hit are distinct signals.

### Visual search

1. User selects a reference image or pastes from clipboard.
2. Show the reference and let the user search the whole image or draw a rectangle/crop. Optional detection can suggest region boxes.
3. Preprocess orientation/color/size consistently with indexed assets.
4. Create query embedding locally; run vector nearest-neighbor retrieval.
5. Use perceptual hash for exact/near-duplicate detection where useful.
6. Re-rank candidates using a second-stage similarity/model or crop comparison; calibrate scores on a benchmark corpus.
7. Return exact duplicate and semantically similar results as separate groups or labels.
8. Every result links to the original file plus precise locator: page, slide, sheet, PSD layer or video timestamp and optional bounding box.
9. Preview the source with the matching region highlighted.

General image embeddings are good for semantic similarity but may not reliably match a specific individual face. Do not promise identity search. If face-region similarity is later considered, it must be a separately enabled local feature with privacy review, retention controls and clear user notice; never label a person or infer their identity.

### Video search

Extract a configurable sample of frames and scene-change keyframes. Index each frame as a visual asset with the original video path and timestamp. Return the video with a seek-to-timestamp action. Do not decode the entire video at full frame rate by default.

## 7. User experience

Primary navigation:
- Dashboard
- Text Search
- Visual Search
- Indexed Locations
- Indexing Queue / Errors
- Search History / Favorites
- Settings / Privacy

Visual-search screen:
- drag/drop, file picker and clipboard paste;
- image canvas with movable/resizable selection rectangle;
- whole-image vs selected-region toggle;
- optional object/region suggestions;
- match threshold and exact-duplicate toggle;
- grid/list results and filters;
- side-by-side preview with source location and open-folder action.

Universal results:
- thumbnail, file name, type, size, modified date, root/path, match type, match score and match locator;
- preview pane for supported formats;
- copy path, open file, open containing folder, favorite, export results;
- clearly distinguish preview unavailable, source offline, access denied and metadata-only results.

## 8. Security, privacy and reliability controls

- Only explicit user-selected roots are indexed.
- Respect Windows permissions. If access is denied, report it; never bypass it.
- Use authenticated current-user access to SMB shares. Avoid saving passwords; if credentials are necessary, use Windows Credential Manager and obtain explicit consent.
- Keep all content processing local by default. No telemetry containing paths, filenames, extracted text, thumbnails or reference images.
- Store caches in per-user application data with appropriate ACLs. Provide clear cache/index deletion controls.
- Treat parsers and archives as untrusted. Enforce file size, decompressed size, nesting depth, CPU/time limits, page/frame limits and safe temp paths.
- Do not execute scripts, macros, links or embedded content during indexing or preview.
- Avoid path traversal and symlink/junction escapes. Validate resolved paths against configured roots.
- Do not expose the local API to LAN interfaces. If enabled, bind only to loopback and use a per-install random token.
- Redact paths and document text from logs by default; provide opt-in diagnostic export with user review.
- Include privacy notice and settings for disabling OCR, embeddings, image extraction and any future face-region function.

## 9. Development phases and quality gates

### Phase 0 — Repository, decisions and proof of feasibility
Deliver: repository layout, ADRs, solution skeleton, CI/build scripts, sample fixtures and an implementation status file.
Acceptance:
- Architecture decisions recorded for UI, index, OCR, image model, vector index and packaging.
- Dependency/license/security review template created.
- App builds and launches as an empty WPF shell.
- Python worker health check works locally, or a documented decision delays process isolation.
- No disk scanning occurs until a root is added.

### Phase 1 — File roots, metadata indexing and safe job engine
Deliver: add/remove root UI, local/UNC path validation, recursive enumeration, metadata DB, progress, cancellation, pause/resume, errors and first-run demo data.
Acceptance:
- Indexes a user-selected test folder and reports file count accurately.
- Works with denied files and long paths without crashing.
- Does not follow junctions/symlinks outside scope.
- Source file hashes/timestamps remain unchanged.
- Re-run does not duplicate records.

### Phase 2 — Full-text search and previews
Deliver: filename/path search, DOCX/XLSX/PPTX/PDF/TXT/CSV extraction adapters, FTS5, filters, snippets, preview pane.
Acceptance:
- Search finds phrases inside each supported fixture, not only filenames.
- Workbook sheet/cell and PDF page locators are preserved where feasible.
- Corrupt/unsupported files show actionable status without stopping indexing.

### Phase 3 — OCR and Arabic/English search
Deliver: image and scanned-PDF OCR, language selection, normalization, confidence and snippets.
Acceptance:
- Arabic and English benchmark set meets an agreed accuracy threshold.
- Original and normalized text are retained separately.
- Poor-quality OCR is marked low-confidence, never silently treated as exact text.

### Phase 4 — Image visual search MVP
Deliver: image indexing, embeddings, vector index, whole-image search, crop/region selection, similarity result grid and preview.
Acceptance:
- Search finds exact/near duplicates and semantically similar items in a labelled test set.
- Exact duplicate and visual similarity are distinct result types.
- Crop search is evaluated against whole-image search.
- No image content leaves the local machine.

### Phase 5 — Embedded assets, PSD and document visual search
Deliver: images extracted from PDF/Office documents, page/slide/sheet navigation, PSD composite and supported layers.
Acceptance:
- A known image embedded in a fixture is found and maps to its parent document and exact page/slide/layer.
- Unsupported PSD features are reported honestly; original PSD remains untouched.

### Phase 6 — Video frame search
Deliver: video metadata, sampled frames, visual embeddings, timestamps, seek preview.
Acceptance:
- A known scene in the fixture video returns the correct file and timestamp within the configured sampling tolerance.
- Frame sampling limits and CPU budgets are enforced.

### Phase 7 — Incremental monitoring, scale and packaging
Deliver: local change monitoring, periodic share reconciliation, performance tools, backup/rebuild index, installer and upgrade strategy.
Acceptance:
- Created/modified/deleted file changes reconcile without duplicate or stale records.
- Watcher overflow falls back safely to reconciliation.
- Benchmarks run on representative file counts and document the hardware used.
- Installer starts without requiring Python development tools.

### Phase 8 — Hardening and optional advanced features
Deliver: signed build, threat review, export, favorites, optional object detection, optional face-region similarity only after approval.
Acceptance:
- Security review has no unresolved critical/high findings.
- Permission boundaries and opt-in privacy settings are tested.
- All user-facing unsupported/failed states are understandable.

## 10. Performance targets to validate, not assume

Claude must establish baseline hardware and sample corpus before setting hard guarantees. Proposed initial engineering targets:
- UI remains responsive during indexing; progress updates at least once per second.
- Metadata enumeration and extraction use bounded concurrency and cancellation.
- Text search over a 100k-file sample returns in under 1 second on the agreed reference PC after warm-up, as a target to test rather than a promise.
- Visual query returns first page within a few seconds on CPU for a small/medium corpus; benchmark GPU acceleration separately.
- Indexing throughput, memory usage, model size and disk cache growth are measured per file family.

## 11. Suggested repository layout

```text
FileLens/
├── CLAUDE.md
├── README.md
├── FileLens.sln
├── global.json
├── Directory.Build.props
├── docs/
│   ├── TECHNICAL-IMPLEMENTATION-PLAN.md
│   ├── FORMAT-CAPABILITY-MATRIX.md
│   ├── IMPLEMENTATION-STATUS.md
│   ├── decisions/
│   │   ├── ADR-0001-desktop-stack.md
│   │   ├── ADR-0002-index-storage.md
│   │   └── ADR-0003-ai-worker.md
│   └── test-plan/
├── src/
│   ├── FileLens.Desktop/          # WPF views, ViewModels, app startup
│   ├── FileLens.Application/      # use cases, DTOs, orchestration
│   ├── FileLens.Domain/           # entities, value objects, interfaces
│   ├── FileLens.Infrastructure/   # SQLite, filesystem, logging, adapters
│   ├── FileLens.Indexing/         # jobs, queues, incremental reconciliation
│   ├── FileLens.Search/           # FTS, ranking, query parsing, vector adapter
│   └── ai-worker/                 # Python OCR/vision worker and pyproject.toml
├── tests/
│   ├── FileLens.UnitTests/
│   ├── FileLens.IntegrationTests/
│   ├── FileLens.PerformanceTests/
│   └── ai-worker-tests/
├── testdata/                      # synthetic/non-sensitive fixtures only
├── scripts/
│   ├── bootstrap.ps1
│   ├── build.ps1
│   ├── test.ps1
│   └── run-dev.ps1
└── .claude/
    ├── skills/
    ├── agents/
    └── settings.json              # optional shared safe permissions/hooks
```

## 12. First implementation task for Claude

Do not build indexing or AI in the first pass. First inspect the environment and repo, propose ADRs, and create the solution skeleton with an empty WPF screen, configuration, logging, test projects, Python worker health-check stub, README and status tracking. Build and launch it. Then stop and report the verified baseline before beginning Phase 1.
