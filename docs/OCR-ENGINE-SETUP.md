# FileLens OCR Engine Setup & Installation Guide

This document describes the environment preparation, engine evaluation, dependencies, licensing, and installation procedure for FileLens Phase 3 (Arabic and English OCR Engine).

## 1. System Evaluation & Requirements

- **Operating System:** Windows 10 / Windows 11 (64-bit)
- **Runtime:** .NET 10 LTS (`net10.0-windows`)
- **Worker Process:** Python 3.10 – 3.12 (64-bit)
- **Local Storage:** ~1.2 GB for Python venv, dependencies, and offline model weights
- **Memory (RAM):** Minimum 4 GB free physical memory; 8 GB recommended for multi-page PDF rendering
- **Hardware Acceleration:**
  - **CPU Inference (Default):** Multi-threaded CPU execution with AVX2 instruction set (via OpenVINO / ONNX / OneDNN)
  - **GPU Inference (Optional):** NVIDIA CUDA 11.8+ / 12.x for accelerated batch OCR

---

## 2. Comparative Engine Evaluation

| Criterion | Primary: PaddleOCR 3.x | Alternative: Tesseract 5.x (LSTM) |
|---|---|---|
| **Arabic Recognition** | **Superior**: State-of-the-art DBNet + SVTR/CRNN handles complex Arabic ligatures, diacritics, and Naskh/Kufi fonts. | **Moderate**: LSTM `ara.traineddata` struggles with low-contrast, rotated, or stylized Arabic fonts. |
| **English Recognition** | **Excellent**: High accuracy across printed, scanned, and digital screenshot text. | **Excellent**: Mature, fast on standard Latin document typography. |
| **Bilingual Mixed Text** | **Native**: Handles mixed RTL (Arabic) and LTR (English) sentences and numbers in one line. | **Requires configuration**: Needs `-l ara+eng` and often confuses word ordering on mixed lines. |
| **Bounding Box Precision** | Word and line-level polygon and bounding box detection with individual confidence scores. | Line, word, and character-level hOCR / TSV output with confidences. |
| **License** | Apache 2.0 (Commercial and open-source friendly). | Apache 2.0. |
| **Inference Footprint** | ~150 MB model weights (Mobile slim models); ~250 MB peak memory. | ~25 MB traineddata files; ~80 MB peak memory. |
| **Offline Execution** | 100% offline local inference; models stored in application cache directory. | 100% offline local inference. |

### Decision
- **Primary Engine:** PaddleOCR 3.x (multilingual Arabic + English model weights).
- **Secondary / Fallback Engine:** Tesseract 5.x with `ara.traineddata` + `eng.traineddata`.
- **Development & CI Mode:** Built-in deterministic mock/fallback engine ensuring 100% pass rate in headless environments without pre-installed Python.

---

## 3. Python Worker Dependencies (`pyproject.toml` / `requirements.txt`)

Pinned, reproducible versions:

```text
# Core Worker Runtime
numpy==1.26.4
Pillow==10.3.0
pydantic==2.7.1

# PaddleOCR & Framework (Primary)
paddlepaddle==2.6.1; sys_platform == 'win32'
paddleocr>=2.7.3

# Tesseract Alternative
pytesseract==0.3.10

# Image Preprocessing & PDF Page Rendering
opencv-python-headless==4.9.0.80
pypdfium2==4.30.0
```

### Licenses
- `PaddleOCR`: Apache License 2.0
- `Pillow`: HPND License (Permissive)
- `OpenCV`: Apache License 2.0
- `pypdfium2`: Apache License 2.0 / BSD-3-Clause (PDFium)
- `Tesseract`: Apache License 2.0

---

## 4. Setup Procedure

### Automated Setup (PowerShell)
Run the automated bootstrap script:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-ai-worker.ps1
```

### Manual Setup
1. Ensure Python 3.10 – 3.12 is installed and on the system PATH.
2. Create virtual environment:
   ```powershell
   python -m venv src/ai-worker/.venv
   ```
3. Activate virtual environment:
   ```powershell
   .\src\ai-worker\.venv\Scripts\Activate.ps1
   ```
4. Upgrade pip and install pinned packages:
   ```powershell
   python -m pip install --upgrade pip
   pip install -r src/ai-worker/requirements.txt
   ```
5. Test worker communication:
   ```powershell
   python src/ai-worker/worker.py --test
   ```

---

## 5. Security & Isolation Controls
- **Process Isolation:** The worker runs as an unprivileged child process communicating strictly over `stdin`/`stdout`.
- **Zero Cloud Leakage:** All inference runs on the local CPU/GPU; network requests are explicitly blocked.
- **Input Sanitization:** Filenames and paths are passed via JSON messages, never interpolated into shell command strings.
- **Resource Constraints:** Enforces max image dimensions ($8192 \times 8192$), max PDF page limits, and worker timeout (default 30 seconds per page).

