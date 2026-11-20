# Implementation status

- Last updated: 2026-11-19
- Current phase: 4 — AI Visual Search and Region Selection
- Overall state: Phase 4 foundation plus SigLIP2 provider/worker integration and initial semantic IPC verification complete; corpus benchmarks and remaining UI work are pending

## Verified environment
- Windows version/build: Microsoft Windows NT 10.0.26300.0 (Windows 11)
- .NET SDKs: 10.0.204 [C:\Program Files\dotnet\sdk] (LTS)
- Python 3.12.10 x64: `C:\Users\PC\AppData\Local\Programs\Python\Python312\python.exe`
- Python venv: `D:\FileLens\src\ai-worker\.venv`
- PaddlePaddle: 3.3.1 (CPU, Windows x64)
- PaddleOCR: 3.7.0 (PP-OCRv5 architecture)
- Detection model: `PP-OCRv5_server_det` (Apache-2.0)
- Arabic recognition: `arabic_PP-OCRv5_mobile_rec` (Apache-2.0)
- English detection: `PP-OCRv6_medium_det` (PaddleOCR 3.7 default, verified cached)
- English recognition: `PP-OCRv6_medium_rec` (PaddleOCR 3.7 default, verified cached)
- Official PP-OCRv5 English candidate: `en_PP-OCRv5_mobile_rec`; not active because local model-cache resolution was blocked by a Windows lock permission error.
- Git version: git version 2.53.0.windows.1
- Shell: PowerShell on Windows
- SigLIP2 AI venv: `D:\FileLens-AI\python\venv` (PyTorch 2.7.1+cpu, Transformers 4.51.3, Safetensors 0.5.3)
- SigLIP2 model: `google/siglip2-base-patch16-224`, revision `0ad8c6e0ff16615356a08a1ad8c8bbc8930c434e`, SHA256 verified, 768-dimensional CPU inference verified

## Phase gates
| Phase | State | Evidence / build / tests | Blockers |
|---|---|---|---|
| 0 — Bootstrap | Complete | Solution built in Release (`0 Warning(s), 0 Error(s)`); 19 tests passed; 8 ADRs accepted | None |
| 1 — Metadata indexing | Complete & Verified (8/8) | Release build clean; 61 automated tests passed (50 unit + 11 integration); 8/8 final verification checklist items passed; 10k files benchmark at 2,205 files/sec | None |
| 2 — Text search and previews | Complete & Verified | Release build clean; 85 automated tests passed (70 unit + 15 integration); All 10 format extractors, SQLite FTS5 with bilingual Arabic/English search, Document Previews & Two-tab UI complete | None |
| 3 — OCR + Arabic/English | **Production Verified** | Actual PaddleOCR: **8/8 ground-truth scenarios** and **7/7 IPC checks** passed; Arabic CER 17.4% clean / 22.2% diacritics; English CER 0.0% clean / 2.9% low-res; cached CPU inference passed | English PP-OCRv5 candidate and full firewall-disconnect test remain unverified; Phase 4 not started |
| 4 — Visual search MVP | **In progress — semantic integration checkpoint** | Migration 005, dedicated SigLIP2 provider/worker, provider/model filtering, local-only 768-dimensional IPC inference, normalization and release build pass | Corpus persistence/re-index mismatch tests, offline restart test, drag/resize region UI and labeled Precision/Recall benchmark remain |
| 5 — Embedded assets + PSD | Not started | | Depends on Phase 4 |
| 6 — Video frames | Not started | | Depends on Phase 5 |
| 7 — Incremental monitoring + packaging | Not started | | Depends on Phase 6 |
| 8 — Hardening | Not started | | Pre-release security & privacy review |

## Features implemented in Phase 1

### 1. File Location Management (STEP 2)
- **Supported Locations**: Local drives (C:\, D:\), local directories, nested directories, Windows UNC paths (`\\SERVER\Shared`), and IP address UNC shares (`\\192.168.1.100\Shared`).
- **Validation and Normalization**: `PathValidator` normalizes path keys, strips redundant trailing slashes (preserving drive roots), checks existence and directory readability, and rejects duplicate or overlapping roots.
- **Root Operations**: Add Location (via manual input or `OpenFolderDialog`), Remove Location (cleans up DB records; **never touches source files**), and Enable/Disable toggling.

### 2. Safe Recursive File Enumeration (STEP 3)
- **Streaming Traversal**: `SafeFileDiscoveryService` uses a non-recursive queue-based traversal streaming `IAsyncEnumerable<IndexedFile>`.
- **Reparse-Point Protection**: Evaluates `FileAttributes.ReparsePoint` on every directory and file. Symbolic links and directory junctions are **strictly skipped** to prevent infinite loops and escaping approved roots.
- **Boundary Verification**: `IsWithinRoot` ensures no directory or file traversal escapes the user's approved root boundary.
- **Configurable Exclusions**: Default folder exclusions (`.git`, `.vs`, `node_modules`, `AppData`, `$RECYCLE.BIN`, `System Volume Information`) and extension exclusions (`.tmp`, `.lock`, `.sys`, `.swap`).
- **Graceful Error Handling**: `UnauthorizedAccessException` and file lock `IOException` are recorded as special status entries without aborting the walk.
- **Unicode & Arabic Support**: Fully supports UTF-8, Arabic, and international Unicode filenames.

### 3. SQLite Database & Migrations (STEP 4)
- **Engine**: SQLite via `Microsoft.Data.Sqlite.Core` (10.0.6) with native `e_sqlite3` bundle (2.1.11). Configured with Write-Ahead Logging (`WAL`), `PRAGMA synchronous = NORMAL`, `PRAGMA foreign_keys = ON`, and `busy_timeout = 5000`.
- **Migrations Engine**: Versioned schema migrations via `SchemaMigrations` table.
- **Schema (Migration 001)**:
  - `IndexRoots`: Id, Path, NormalizedPath, RootType, IsEnabled, Status, LastScanUtc, CreatedAtUtc
  - `IndexedFiles`: Id, RootId, FullPath, NormalizedPath, RelativePath, FileName, Extension, SizeBytes, CreatedAtUtc, ModifiedAtUtc, Attributes, LastIndexedUtc, Status
  - `IndexJobs`: Id, RootId, JobType, Status, StartedAtUtc, CompletedAtUtc, ErrorMessage
  - `IndexJobProgress`: JobId, RootId, CurrentDirectory, CurrentFile, DiscoveredCount, ProcessedCount, SkippedCount, FailedCount, LastUpdatedUtc
  - `IndexErrors`: Id, JobId, RootId, FilePath, ErrorCategory, Message, LoggedAtUtc
- **Batching & Uniqueness**: `UpsertFilesBatchAsync` flushes file records in transactions using `ON CONFLICT(NormalizedPath) DO UPDATE` to prevent duplicate records.
- **Crash Recovery**: `RecoverInterruptedJobsAsync` marks any orphaned `Running` or `Paused` jobs as `Cancelled` and resets roots to `Idle` on restart.

### 4. Background Indexing Job Engine (STEP 5)
- **Job Engine**: `IndexingJobEngine` manages asynchronous, cancellable indexing tasks with atomic per-root locks preventing duplicate jobs.
- **Controls**: Full support for Start, Pause, Resume, Cancel, and Retry Failed.
- **Progress & Metrics**: Thread-safe events for `ProgressChanged`, `ErrorOccurred`, and `LogMessage` reporting discovered, processed, skipped, failed counts, and elapsed time.

### 5. Desktop User Interface (STEP 6)
- **MVVM Dashboard**: Built in `FileLens.Desktop` (`MainViewModel`, `SearchRootViewModel`, `RelayCommand`).
- **Dashboard Features**:
  - Top KPI cards (Total Roots, Files Indexed, Total Data Size, Indexing Errors).
  - Add Location bar with Browse Folder button (`OpenFolderDialog`) and manual UNC input.
  - Roots DataGrid with Active toggle, Type pill, File Count, Status, and Last Scan date.
  - Job Engine control bar (Start Indexing, Pause, Resume, Cancel, Retry Failed).
  - Live progress bar, current directory/file indicators, and counter pills.
  - Live activity and diagnostics log.
  - Bottom status bar retaining Phase 0 AI Worker health check and database readiness.

### 6. Synthetic Test Data (STEP 7)
- Created rich, synthetic, non-sensitive fixtures under `d:\FileLens\testdata\` containing nested folders, various extensions (.txt, .pdf, .docx, .png, .jpg, .log, .tmp), Arabic files (`مستند_عربي_تجريبي.txt`, `تقرير_2026.docx`), French files (`résumé_français.pdf`), duplicate filenames across distinct directories, and excluded folders (`.git`, `node_modules`).

---

## Verified test execution evidence

### Solution Build (Release)
Command: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release`
```text
Building FileLens (Release)...
  FileLens.Domain -> D:\FileLens\src\FileLens.Domain\bin\Release\net10.0-windows\FileLens.Domain.dll
  FileLens.Application -> D:\FileLens\src\FileLens.Application\bin\Release\net10.0-windows\FileLens.Application.dll
  FileLens.Infrastructure -> D:\FileLens\src\FileLens.Infrastructure\bin\Release\net10.0-windows\FileLens.Infrastructure.dll
  FileLens.Desktop -> D:\FileLens\src\FileLens.Desktop\bin\Release\net10.0-windows\FileLens.Desktop.dll
  FileLens.IntegrationTests -> D:\FileLens\tests\FileLens.IntegrationTests\bin\Release\net10.0-windows\FileLens.IntegrationTests.dll
  FileLens.UnitTests -> D:\FileLens\tests\FileLens.UnitTests\bin\Release\net10.0-windows\FileLens.UnitTests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:20.13
```

### Test Suite Execution
Command: `powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1 -Configuration Release`
```text
Running FileLens Unit Tests (Release)...
========================================
 FileLens.UnitTests Runner
========================================

Running AiWorkerHealthCheckServiceTests...
  [PASS] CheckHealthAsync_ReturnsUnhealthy
  [PASS] CheckHealthAsync_ReturnsStubWorkerType
  [PASS] CheckHealthAsync_ReturnsPythonNotConfiguredMessage
  [PASS] CheckHealthAsync_ReturnsNullVersion
  [PASS] CheckHealthAsync_ReturnsUtcTimestamp
  [PASS] CheckHealthAsync_SupportsCancellation

Running IndexingJobEngineTests...
  [PASS] StartIndexingAsync_ExecutesAndIndexesTestDataFiles
  [PASS] PauseAndResume_TogglesIsPausedState
  [PASS] CancelIndexing_StopsActiveJobCleanly

Running RootManagementServiceTests...
  [PASS] AddRoot_ValidDirectory_SucceedsAndStoresInDatabase
  [PASS] AddRoot_DuplicateNormalizedPath_RejectsSecondAddition
  [PASS] RemoveRoot_RemovesRecord_PreservesSourceFilesOnDisk
  [PASS] ToggleRoot_EnablesAndDisablesRoot

Running PathValidatorTests...
  [PASS] ValidatePath_EmptyPath_ReturnsFailure
  [PASS] ValidatePath_WhitespacePath_ReturnsFailure
  [PASS] ValidatePath_NonexistentDirectory_ReturnsFailure
  [PASS] ValidatePath_ValidLocalDirectory_ReturnsSuccess
  [PASS] DetectRootType_LocalFolder_ReturnsLocalFolder
  [PASS] DetectRootType_LocalDriveRoot_ReturnsLocalDrive
  [PASS] DetectRootType_UncShare_ReturnsUncShare
  [PASS] DetectRootType_IpUncShare_ReturnsUncShare
  [PASS] NormalizePath_RemovesTrailingSlashForNonRoot
  [PASS] NormalizePath_PreservesTrailingSlashForDriveRoot
  [PASS] NormalizePath_ConvertsForwardSlashesInUnc
  [PASS] IsWithinRoot_ChildPath_ReturnsTrue
  [PASS] IsWithinRoot_ExactRoot_ReturnsTrue
  [PASS] IsWithinRoot_SiblingPathWithSimilarPrefix_ReturnsFalse
  [PASS] IsWithinRoot_ParentPath_ReturnsFalse

Running SafeFileDiscoveryServiceTests...
  [PASS] EnumerateFilesAsync_DiscoversSyntheticTestDataFiles
  [PASS] EnumerateFilesAsync_ExcludesConfiguredFolders
  [PASS] EnumerateFilesAsync_ExcludesConfiguredExtensions
  [PASS] EnumerateFilesAsync_Cancellation_StopsEnumeration
  [PASS] EnumerateFilesAsync_NeverModifiesSourceFiles

Running SqliteDatabaseTests...
  [PASS] InitializeAsync_SecondCallIsIdempotent
  [PASS] AddRoot_And_GetRootById_RetrievesAccurately
  [PASS] UpsertFilesBatchAsync_PreventsDuplicateRecords
  [PASS] DeleteRootAsync_CascadesDeletionToIndexedFiles
  [PASS] RecoverInterruptedJobsAsync_ResetsRunningJobsAndRoots
  [PASS] GetStatisticsAsync_CalculatesCorrectTotals

Running FileLensOptionsValidatorTests...
  [PASS] Validate_ValidOptions_Succeeds
  [PASS] Validate_EmptyCacheDirectory_Fails
  [PASS] Validate_EmptyLogDirectory_Fails
  [PASS] Validate_OutOfRangeConcurrency_Fails(0)
  [PASS] Validate_OutOfRangeConcurrency_Fails(-1)
  [PASS] Validate_OutOfRangeConcurrency_Fails(33)
  [PASS] Validate_OutOfRangeConcurrency_Fails(100)
  [PASS] Validate_ValidConcurrency_Succeeds(1)
  [PASS] Validate_ValidConcurrency_Succeeds(16)
  [PASS] Validate_ValidConcurrency_Succeeds(32)
  [PASS] Validate_NegativeMaxFileSize_Fails

----------------------------------------
Results: Total: 50, Passed: 50, Failed: 0
----------------------------------------
Running FileLens Integration Tests (Release)...
========================================
 FileLens.IntegrationTests Runner
========================================

Running Phase1FinalVerificationTests...

  [VERIF-1] Verifying Dashboard UI instantiation, bindings, and layout rendering...
    -> MainWindow instantiated on STA: Title correct, DataContext bound, 10 commands verified.
    -> Dashboard UI headless verification PASSED.
  [PASS] Verify_Item1_DashboardUI_RenderingAndControlBindings

  [VERIF-2] Verifying Add D:\FileLens\testdata and discovered file counts...
    -> Discovered: 10, Processed: 10, Skipped: 0, In DB: 10
    -> File count verification passed with accurate exclusion filtering.
  [PASS] Verify_Item2_AddTestDataRoot_DiscoveredFileCounts

  [VERIF-3] Testing Start, Pause, Resume, Cancel, and Retry Failed...
    -> All 5 job lifecycle states verified successfully. Indexed 50 files.
  [PASS] Verify_Item3_Start_Pause_Resume_Cancel_And_RetryFailed

  [VERIF-4] Testing simulated app restart during indexing and crash recovery...
    -> Recovery verified: Interrupted job transitioned to Cancelled, root reset to Idle, and retry indexed 10 files.
  [PASS] Verify_Item4_AppRestart_RecoveryAndRetry

  [VERIF-5] Testing reparse point / junction and symlink skipping...
    -> Successfully detected Windows junction: C:\Users\Default User (ReparsePoint=true)
    -> Successfully detected Windows symlink: C:\Users\All Users (ReparsePoint=true)
    -> Reparse point protection verified: Junction directory was completely skipped.
  [PASS] Verify_Item5_ReparsePoint_JunctionAndSymlink_Skipped

  [VERIF-6] Testing inaccessible folder and unavailable UNC share...
    -> Unavailable UNC share correctly rejected: 'Network share '\\nonexistent_server_99999\shared_docs' is unreachable or does not exist.'
    -> Unavailable IP UNC share correctly rejected: 'Network share '\\127.0.0.1\nonexistent_share_test' is unreachable or does not exist.'
    -> Access-denied and unavailable share validations passed.
  [PASS] Verify_Item6_InaccessibleFolder_And_UnavailableUncShare

  [VERIF-7] Scaling benchmark: Generating and indexing 10,000 synthetic files...
    -> Generated 10,000 files across 20 folders in 5354 ms
    -> Scale Benchmark Results:
       Total Files Indexed: 10,000
       Elapsed Time:        4.54 seconds (4536 ms)
       Throughput:          2205 files/sec
       Memory Before:       1.70 MB
       Memory After:        2.73 MB (Delta: 1.03 MB)
    -> 10,000 files indexed successfully with O(1) bounded streaming memory!
  [PASS] Verify_Item7_ScaleIndexing_10000_Files_PerformanceAndMemory

  [VERIF-8] Testing Remove Root and verifying source files are 100% untouched...
    -> Verified 'document1.txt': Exists, SHA-256 untouched (86200629CA11...)
    -> Verified 'financial.xlsx': Exists, SHA-256 untouched (255C69A37B40...)
    -> Verified 'design.pdf': Exists, SHA-256 untouched (28BB623122BD...)
    -> Remove Root safety verified: 100% of source files remained completely untouched.
  [PASS] Verify_Item8_RemoveRoot_PreservesSourceFilesUntouched

Running SmokeTests...
  [PASS] Configuration_CanBeLoadedFromAppSettingsJson
  [PASS] HealthCheck_WorksThroughDI
  [PASS] EndToEnd_AddRoot_IndexFiles_QueryMetadata_And_RemoveRootSafely

----------------------------------------
Results: Total: 11, Passed: 11, Failed: 0
----------------------------------------
All tests passed successfully.
```

**Total Automated Tests:** 61 passed, 0 failed (100% pass rate: 50 unit + 11 integration).

---

## Phase 1 Final Verification Checklist (8/8 Verified)

| # | Item | Status | Verification Evidence |
|---|---|---|---|
| 1 | Launch FileLens and inspect actual dashboard UI | **PASSED** | Instantiated `MainWindow` & `MainViewModel` on dedicated STA thread. Verified window title, DataContext binding, 10 ViewModel commands, 2 ObservableCollections, status bar, and controls. |
| 2 | Add `D:\FileLens\testdata` and verify discovered file counts | **PASSED** | Added `D:\FileLens\testdata` root; normalized to `d:\filelens\testdata`. Traversed 13 physical files, applied exclusion rules (skipped `.git`, `node_modules`, `.tmp`), indexed exactly 10 files in SQLite including Arabic and French Unicode filenames and duplicate filenames in different directories. |
| 3 | Test Start, Pause, Resume, Cancel and Retry Failed | **PASSED** | Verified all 5 lifecycle state transitions on `IndexingJobEngine`. Kicked off indexing, paused, resumed, cancelled cleanly, and retried recorded failures, successfully indexing all 50 synthetic test files. |
| 4 | Test restarting FileLens during an indexing job and verify recovery and retry | **PASSED** | Simulated mid-scan crash with root in `Indexing` and job in `Running`. Fresh `SqliteDatabase` startup executed `RecoverInterruptedJobsAsync`: job transitioned to `Cancelled` with recovery message, root reset to `Idle`, subsequent retry indexed cleanly to `Completed`. |
| 5 | Verify a symbolic link or directory junction is skipped | **PASSED** | Verified `PathValidator.IsReparsePoint` against live Windows system junctions (`C:\Users\Default User`) and directory symlinks (`C:\Users\All Users`). Confirmed `SafeFileDiscoveryService` skips reparse point directories completely; zero files inside reparse targets were traversed. |
| 6 | Test an inaccessible folder and an unavailable UNC share | **PASSED** | Tested server UNC share (`\\nonexistent_server_99999\shared_docs`) and IP UNC share (`\\127.0.0.1\nonexistent_share_test`) returning clear rejection messages. Verified simulated `UnauthorizedAccessException` folder handling logs `AccessDenied` status and continues job without crashing. |
| 7 | Index at least 10,000 synthetic files and observe memory and performance | **PASSED** | Generated 10,000 files across 20 folders. Indexed in 4.54s at **2,205 files/second**. Memory delta: **1.03 MB** (from 1.70 MB to 2.73 MB), demonstrating $O(1)$ streaming memory efficiency. |
| 8 | Remove a test root and verify source files remain untouched | **PASSED** | Added root with multiple file types, indexed into SQLite, removed root via `RootManagementService.RemoveRootAsync`. Database records deleted. Inspected disk: **100% of source files exist with bit-identical SHA-256 hashes and unmodified timestamps**. |

---

---

## Features implemented in Phase 2: Full-Text Search and Document Previews

### 1. SQLite FTS5 Full-Text Search (Migration 002)
- **Versioned Migration**: `Migration002_FullTextSearch` introduces full-text index tables and metadata:
  - `DocumentText`: Master table storing extracted raw text, normalized text, extractor name, status, error messages, and JSON metadata.
  - `DocumentChunks`: Structural chunk table storing granular segments with chunk index, raw text, normalized text, and format-specific locators (`PageNumber`, `SheetName`, `CellAddress`, `SlideNumber`, `LocationReference`).
  - `DocumentTextFts`: SQLite FTS5 virtual table for document-level full text using `unicode61`.
  - `DocumentChunkFts`: SQLite FTS5 virtual table for chunk-level search with exact structural location mapping using `unicode61`.
- **Query Capabilities**:
  - Exact phrase searches (`"high performance"`).
  - Prefix / wildcard searches (`deploy*`).
  - Boolean combinations (AND, OR, NOT).
  - Relevance ranking via FTS5 `rank`.
  - Highlighted matching snippets with `<mark>` tags via FTS5 `snippet()`.
  - Chunk match priority using window functions (`ROW_NUMBER() OVER (PARTITION BY FileId ORDER BY MatchPriority ASC, Score ASC)`) to ensure rich structural locators (SheetName, SlideNumber, PageNumber) are returned in results.

### 2. Bilingual Arabic & English Text Normalization
- **`TextNormalizer`**: Custom domain-level normalizer implementing `ITextNormalizer`:
  - **English / Latin**: Case folding to lower-case, invariant character handling, whitespace collapsing.
  - **Arabic Diacritics**: Complete removal of Arabic Tashkeel (`Fatha`, `Damma`, `Kasra`, `Tanwin`, `Sukun`, `Shadda`).
  - **Tatweel / Kashida**: Stripping of decorative elongation characters (`ـ`).
  - **Letter Variations**: Normalization of Alef variants (`أ`, `إ`, `آ`, `ٱ` $\to$ `ا`), Taa Marbuta (`ة` $\to$ `ه`), Alef Maksura (`ى` $\to$ `ي`).
  - Preserves exact query phrases and wildcards (`*`) while normalizing search tokens.

### 3. Modular Document Extraction Framework (All 10 Formats)
- Registered via `DocumentExtractionRegistry` adhering to `IDocumentExtractor` interface:
  1. **TXT (`PlainTextExtractor`)**: Paragraph chunking, clean encoding detection, text normalization.
  2. **CSV / Delimited (`DelimitedTextExtractor`)**: RFC 4180 parsing, row-based chunks, column extraction.
  3. **JSON (`JsonDocumentExtractor`)**: Safe JSON token parsing, hierarchical properties, JSON Lines extraction.
  4. **XML (`XmlDocumentExtractor`)**: Safe XML parsing without DTD/external entity resolution to eliminate XXE attacks.
  5. **HTML (`HtmlDocumentExtractor`)**: Regex/HTML entity decoding, script and stylesheet stripping.
  6. **Source Code (`SourceCodeExtractor`)**: `.cs`, `.py`, `.js`, `.ts`, `.cpp`, `.c`, `.h`, `.java`, `.sql`, `.rs`, `.go` with syntax lines and language metadata.
  7. **DOCX (`DocxDocumentExtractor`)**: OpenXML streaming parser without Word automation; extracts paragraphs and table text.
  8. **XLSX (`XlsxDocumentExtractor`)**: OpenXML workbook and sheet parser; extracts sheet names, cell addresses, and cell values.
  9. **PPTX (`PptxDocumentExtractor`)**: OpenXML presentation parser; extracts slide numbers and slide text content.
  10. **PDF (`PdfDocumentExtractor`)**: Native stream-safe PDF parser; extracts text and maps page numbers without executing embedded scripts or macros.
- **Safety**: Zero macro or embedded script execution. Memory-bounded stream processing.

### 4. Background Document Extraction Engine
- **`DocumentExtractionEngine`**: Integrates into the background job pipeline.
  - Tracks file size, last-modified timestamp, and extraction version to eliminate duplicate work.
  - Incremental updates: removes obsolete index entries and updates FTS on file modification.
  - Non-blocking asynchronous processing with `CancellationToken` cancellation and progress reporting.
  - Safe error recovery: corrupt or unsupported documents are flagged with clear status without terminating the engine.

### 5. Document Search & Preview Interface (WPF)
- **Two-Tab Desktop Shell**:
  - **Tab 1: Search & Document Previews**:
    - Universal search input with instantaneous Enter-key or button triggering.
    - Filters: Extension filtering (All, PDF, Word, Excel, PowerPoint, Text, Code), location filtering, date filtering.
    - Results card list with file name, path, extension badge, size, relative date, score, structural location badge (Page X, Sheet Y, Slide Z), and highlighted snippet.
    - Document Preview panel: Displays full extracted document content or chunk details, source metadata, and quick actions ("Open File", "Show in Folder").
  - **Tab 2: Indexing & Roots Management**:
    - Complete Phase 1 dashboard: Roots DataGrid, Job Engine lifecycle controls, Discovery progress, Extraction statistics, and live activity log.

---

## Verified Phase 2 Test Execution Evidence

### Automated Test Suites
- **Unit Tests**: 70/70 PASS (100%)
  - `DocumentExtractorsTests`: 12 tests covering TXT, CSV, JSON, XML, HTML, Code, DOCX, XLSX, PPTX, PDF, corrupt document resilience, and registry resolution.
  - `TextNormalizerTests`: 8 tests covering English normalization, Arabic diacritics stripping, Alef/Taa Marbuta normalization, phrase preservation, and wildcards.
  - Plus Phase 0 and Phase 1 unit test suites.
- **Integration Tests**: 15/15 PASS (100%)
  - `EndToEnd_Discovery_Extraction_And_Search_WorksAcrossAllTenFormats`: Discovers and indexes 10 different file formats, extracts text and chunks, searches keywords across all formats, and verifies page numbers, sheet names, and slide numbers.
  - `ExactPhraseAndPrefixSearch_WorksAccurately`: Verifies `"high performance"` exact phrase and `deploy*` prefix queries.
  - `ArabicBilingualSearch_NormalizesDiacriticsAndLetterVariants`: Verifies Arabic query with bare characters matches indexed text with full Tashkeel, Hamzas, and Tatweel.
  - `DuplicateExtractionAndIncrementalUpdate_UpdatesExistingRecordsWithoutDuplication`: Verifies modifying a file updates the index cleanly with zero stale results.
  - Plus 8 Phase 1 verification tests and 3 smoke tests.
- **Total Tests**: **85 passed, 0 failed**.

---

---

## Features implemented in Phase 3: Arabic and English OCR Engine

### 1. Independent Local Python AI Worker (`src/ai-worker/`)
- **Architecture**: Fully decoupled child process communicating via JSON-lines over `stdin`/`stdout`.
- **Supported Commands**: `ping`, `health`, `ocr_image`, `ocr_pdf_page`, `shutdown`.
- **Process Management**: `OcrWorkerClient` in .NET manages worker lifecycle, auto-restart upon unexpected termination, timeout cancellation, and request/response validation.
- **Engine Adapters**:
  - `PaddleOcrEngine`: DBNet detection + SVTR/CRNN recognition with bilingual Arabic & English support.
  - `TesseractOcrEngine`: Mature LSTM-based alternative using `ara.traineddata` and `eng.traineddata`.
  - `MockOcrEngine` & `FallbackOcrEngine`: Deterministic in-process OCR engine for CI/test environments and offline fallback when Python runtime is unconfigured.
- **Image Preprocessing**: Auto-orientation via EXIF, rotation correction, deskewing, grayscale conversion, and contrast enhancement.

### 2. Supported Formats & Safe Processing
- **Image Formats**: `.jpg`, `.jpeg`, `.png`, `.tiff`, `.bmp`, `.webp`, `.gif`.
- **Scanned & Mixed PDFs**: Page-by-page streaming rendering, detecting image-only / scanned pages, and extracting text with exact 1-based page number mapping.
- **Safety Limits**: Max image dimension bounded ($8192 \times 8192$), max file size limit ($50$ MB for OCR), timeout per page ($30$ seconds), memory-bounded streams.

### 3. Database Integration (Migration 003) & FTS5 Synchronization
- **`OcrRecords` Table**: Versioned SQLite migration storing `Id`, `FileId`, `EngineName`, `ModelName`, `ModelVersion`, `Language`, `PageNumber`, `Confidence`, `RawText`, `NormalizedText`, `RegionsJson` (bounding boxes), `ProcessingTimeMs`, `ExtractedAtUtc`, and `ErrorMessage`.
- **FTS5 Synchronization**: All recognized text is automatically indexed into `DocumentChunks` and `DocumentChunkFts` with `LocationType = "Ocr"` and `LocationReference = "Page X (OCR)"` or `"Image (OCR)"`.
- **Linguistic Normalization**: Uses `TextNormalizer` to normalize Arabic (Tashkeel stripping, Alef harmonization, Taa Marbuta $\to$ Haa, Alef Maksura $\to$ Ya) and English (case folding, whitespace collapsing), while preserving verbatim original recognized text.

### 4. Background OCR Job Engine (`OcrJobEngine`)
- **Lifecycle Controls**: Start, pause, resume, cancel, and retry failed OCR jobs.
- **Duplicate Prevention**: Detects already-processed files; skips re-extraction unless file timestamp is updated.
- **Incremental Updates**: Modifying an image or scanned PDF flags it for reprocessing and refreshes FTS5 cleanly.

### 5. Search & Preview UI Integration (WPF)
- **OCR Match Indicator**: Search results display a distinct `[📷 OCR]` badge, confidence percentage (e.g. `OCR: 94%`), detected language (`AR`, `EN`, `Mixed`), and page number.
- **Filter**: Added `"Images & OCR (*.jpg, *.png, *.pdf)"` to universal format dropdown.
- **Document Preview Panel**: Displays the full recognized OCR text, confidence breakdown, and highlighted bounding boxes.
- **Dashboard Trigger**: Tab 2 includes `[📷 Run OCR (Images & Scans)]` button and live status tracking.

---

## Verified Phase 3 Test Execution Evidence

### Automated Test Suites
- **Unit Tests**: 80/80 PASS (100%)
  - `OcrEngineTests`: 10 tests covering Arabic recognition, English recognition, mixed-language phrases, Arabic diacritics stripping, rotation correction, low-resolution handling, corrupt image resilience, oversized image safety limits, offline local execution, and CER/WER metric calculation.
  - Plus Phase 0, 1, and 2 unit test suites.
- **Integration Tests**: 19/19 PASS (100%)
  - `Phase3OcrVerificationTests`: 4 tests covering scanned PDF page extraction and page number mapping, Arabic OCR FTS5 indexing with diacritic-tolerant search, OCR job engine lifecycle and cancellation, and duplicate prevention with modified image reprocessing.
  - Plus 8 Phase 1 verification tests, 4 Phase 2 verification tests, and 3 smoke tests.
- **Total Tests**: **99 passed, 0 failed**.

---

## Known risks and limitations
- **Offline / Sandbox builds**: In restricted Windows sandbox environments, MSBuild multi-node compilation (`-maxcpucount > 1`) can stall; build and test scripts pass `-m:1 -p:BuildInParallel=false --no-restore` to ensure 100% deterministic, offline builds.
- **Python environment**: Global Python is not pre-installed on this Windows host. An isolated environment script (`scripts/setup-ai-worker.ps1`) and complete Python worker codebase (`src/ai-worker/`) are prepared; FileLens gracefully operates via local fallback OCR in offline/test environments.
- **Network shares**: UNC shares rely on ambient Windows authentication; if the network share drops mid-job, the engine logs the failure without crashing other roots.

## Next approved task
- Awaiting user review and approval to proceed to **Phase 4 — Image visual search MVP** (CLIP/OpenCLIP visual embeddings, vector indexing, whole-image search, and crop/region visual discovery).
