# ADR-0006: Document Format Adapters

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens indexes files across many formats. Each format requires different parsing logic for text extraction and visual asset extraction (embedded images, page renders, slides). A single monolithic parser cannot cover all formats safely or maintainably. Each parser must treat its input as untrusted and enforce resource limits.

## Decision

Use **per-format-family adapters** implementing two core interfaces: `ITextExtractor` and `IVisualAssetExtractor`.

- **`ITextExtractor`:** Extracts structured text chunks from a file. Each adapter declares:
  - Supported file extensions
  - Maximum input file size it will accept
  - Processing timeout
  - Extraction capabilities (full text, page-level, cell-level, etc.)
- **`IVisualAssetExtractor`:** Extracts embedded images, page/slide renders, layers and video frames. Same declaration pattern as above.
- **Phase 2 adapter targets:**

  | Format family | Text library (evaluate) | Visual library (evaluate) | License |
  |---|---|---|---|
  | DOCX/XLSX/PPTX | DocumentFormat.OpenXml | DocumentFormat.OpenXml | MIT |
  | PDF | PdfPig | PdfPig (page rendering TBD) | Apache 2.0 |
  | TXT/CSV/MD | Built-in .NET StreamReader | N/A | N/A |

- **Safety rules for all adapters:**
  - All input is untrusted. No macro, script or embedded-code execution.
  - Enforce per-file size limits and per-operation timeouts.
  - Archive/container formats (ZIP, compound documents) enforce nesting depth and decompressed size limits.
  - Malformed or corrupt files produce an error status, not a crash. Indexing continues for other files.
  - Adapters never modify the source file.
- **Registration:** Adapters register via dependency injection. Format routing uses `IFileClassifier` (extension + magic-byte signature) to select the appropriate adapter.
- **Unknown formats:** Files with no registered adapter receive `MetadataOnly` status with a user-visible explanation. They are still indexed by filename, path, size and dates.

## Alternatives considered

- **Apache Tika (via Java or HTTP):** Broad format coverage in a single tool, but adds a Java runtime dependency and runs as a server process. Violates the no-unnecessary-server-process principle. Could be evaluated as an optional fallback for rare formats in later phases.
- **Single monolithic parser library:** No single .NET library covers all target formats well. Monolithic approaches tend to have lowest-common-denominator extraction quality.
- **IFilter / Windows Search protocol handlers:** Leverages OS-installed parsers, but quality and availability vary by machine, and the API provides limited control over extraction granularity, timeouts and error handling.
- **Aspose (commercial):** High-quality document parsing, but commercial license with per-developer costs. Not suitable for an open/personal project without budget allocation.

## Consequences

- **Positive:** Each adapter is independently testable with format-specific fixtures (valid files, corrupt files, large files, password-protected files).
- **Positive:** New format support is added by implementing the interfaces and registering the adapter — no changes to the indexing pipeline.
- **Positive:** DocumentFormat.OpenXml (MIT) and PdfPig (Apache 2.0) have permissive licenses and active maintenance.
- **Risk:** DocumentFormat.OpenXml does not render page layouts — visual extraction from Office documents may require additional libraries or page-render approaches in Phase 5.
- **Risk:** PdfPig extracts text and images but does not render pages to bitmaps. A separate PDF renderer may be needed for visual preview. Evaluate in Phase 2.
- **Follow-up:** Create test fixtures for each format (valid, corrupt, large, password-protected, macro-containing) before implementing Phase 2 adapters. Document supported vs. unsupported features per format in `FORMAT-CAPABILITY-MATRIX.md`.
