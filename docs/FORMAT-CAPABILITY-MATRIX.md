# Format capability matrix

| Format/family | First capability | Later capability / notes |
|---|---|---|
| Any extension | File name, path, extension, size, timestamps, file signature/type where detectable | Content search only after adapter exists |
| TXT, CSV, JSON, XML, HTML, source code | Full text | Language-aware chunking; avoid executing code |
| DOCX, XLSX, PPTX | Text, tables/sheets/slides, metadata | Extract embedded images and map them to parent document and locator |
| Legacy DOC, XLS, PPT, ODT, RTF | Adapter or safe conversion fallback | Requires dedicated test corpus; no macro execution |
| PDF | Text extraction and page-level preview | Scanned-page OCR, embedded-image extraction, page visual embeddings |
| JPG, JPEG, PNG, TIFF, BMP, GIF, WEBP | Metadata, thumbnail, OCR, image embedding | Crop search, object detection; animated formats need frame policy |
| SVG | Safe text/metadata extraction and sanitized preview | Rasterize for visual embedding; never execute embedded scripts/external resources |
| PSD | Composite preview and supported text/layer metadata | Layer previews/embeddings where parser supports; smart objects, CMYK and unusual bit-depth may be partial |
| AI, EPS, RAW | Metadata and safe preview if a supported renderer exists | Full support depends on licensed renderer/adapter |
| MP4, MOV, AVI, MKV, WMV | Metadata, thumbnail | Sampled keyframes, scene changes, frame embeddings and timestamps via FFmpeg |
| MP3, WAV, AAC, FLAC | Metadata and preview playback | Optional speech-to-text is a separate future feature |
| ZIP, 7Z, RAR | Archive metadata and entry listing | Optional bounded extraction/indexing; block archive bombs and nested archive abuse |

## Status values

Use explicit states such as `Indexed`, `MetadataOnly`, `UnsupportedFormat`, `AccessDenied`, `PasswordProtected`, `Corrupt`, `TooLarge`, `TimedOut`, `SkippedByPolicy`, and `SourceOffline`. A single file may have different states per capability (for example, text indexed but visual preview unavailable).
