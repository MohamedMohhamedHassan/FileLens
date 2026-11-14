# ADR-0005: OCR Engine Strategy

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens must extract text from images and scanned documents, with first-class support for both Arabic and English. OCR accuracy varies significantly by engine, model, scan quality, font and layout. The choice of OCR engine affects accuracy, speed, model size, licensing and language support.

## Decision

Define an **`IOcrEngine` abstraction** in Phase 0. Defer engine selection to Phase 3, decided by benchmark.

- **Interface contract:** `IOcrEngine` accepts an image (or page render), language hints and layout options. It returns structured results preserving:
  - Recognized text with per-word/line confidence scores
  - Source position (bounding boxes relative to the input image)
  - Language/script identification
  - Extraction status and diagnostics
- **Benchmark plan (Phase 3):** Evaluate **PaddleOCR** and **Tesseract** (with LSTM models) on a curated test corpus containing:
  - Printed Arabic text (various fonts, including Naskh and Kufi styles)
  - Printed English text
  - Mixed Arabic + English documents
  - Scanned documents at varying DPI and quality levels
  - Agree on accuracy threshold before selecting the engine
- **Arabic text normalization:** Handled by a separate `ITextNormalizer`, not by the OCR engine. Normalization rules:
  - Alef variants (أ إ آ ٱ → ا) for search normalization
  - Ya variants (ي ى) normalization
  - Optional diacritic stripping (tashkeel)
  - Tatweel (kashida) removal
  - Original OCR output is always stored unchanged; normalized text is a separate search field
- **Phase 0 deliverable:** `IOcrEngine` interface definition only. No OCR library dependency.

## Alternatives considered

- **PaddleOCR:** Strong accuracy on diverse scripts including Arabic, active development, Apache 2.0 license. Requires Python runtime and PaddlePaddle framework (large dependency). Leading candidate.
- **Tesseract 5 (LSTM):** Mature, widely deployed, Apache 2.0 license. Good English accuracy; Arabic accuracy is inconsistent on real-world scans without fine-tuned models. Smaller dependency footprint than PaddleOCR.
- **Windows OCR API (Windows.Media.Ocr):** Built into Windows 10/11, zero dependency. Limited language model quality, no confidence scores, no bounding-box granularity. Insufficient for production-quality Arabic OCR.
- **EasyOCR:** Python-based, supports Arabic. Uses PyTorch (very large dependency). Accuracy competitive but model download size and inference speed are concerns for a desktop app.
- **Commercial OCR (ABBYY, Google Cloud Vision):** High accuracy but cloud-dependent or commercially licensed. Violates local-first and no-cloud-upload-by-default requirements.

## Consequences

- **Positive:** The abstraction allows text search (Phase 2) to ship without any OCR dependency. OCR is additive.
- **Positive:** Benchmark-driven selection avoids committing to an engine that underperforms on the target corpus.
- **Positive:** Separating normalization from OCR output preserves original text for display and audit while enabling flexible search matching.
- **Risk:** Both leading candidates (PaddleOCR, Tesseract) run in the Python worker, making the AI worker a prerequisite for OCR. This is acceptable — OCR is Phase 3, after the worker is established.
- **Risk:** Arabic OCR accuracy may require fine-tuned models or preprocessing (deskew, binarization). Budget time for this in Phase 3.
- **Follow-up:** Curate an Arabic + English test corpus with ground-truth annotations before Phase 3 begins. Define the accuracy threshold that an engine must meet.
