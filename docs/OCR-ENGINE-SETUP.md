# FileLens OCR Engine Setup

Phase 3 uses a local Python worker over JSON-lines IPC. Source files remain read-only; model files and generated data belong in the user/application cache.

## Verified environment (2026-11-18)

- Windows 11 x64; Python 3.12.10 x64 at `C:\Users\PC\AppData\Local\Programs\Python\Python312\python.exe`.
- Python Launcher is installed, but `py -3.12` currently reports no registered installation; the setup script therefore prefers the explicit executable.
- Isolated venv: `src/ai-worker/.venv`.
- PaddlePaddle 3.3.1 CPU; PaddleOCR 3.7.0; PaddleX 3.7.2.
- OpenCV 4.9.0.80 and pypdfium2 4.30.0.
- `paddle.utils.run_check()` passed: PaddlePaddle works on one CPU.

## Repeatable setup

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-ai-worker.ps1
```

The script locates CPython 3.12 x64 by explicit path, Launcher, or a non-Store PATH entry; creates `src/ai-worker/.venv`; installs only the exact versions in `requirements.txt`; and validates PaddlePaddle, PaddleOCR, OpenCV, pypdfium2, and the CPU runtime. It is safe to rerun and does not install packages globally or silently upgrade unrelated packages.

## Models actually resolved

| Language | Detection | Recognition | Result |
|---|---|---|---|
| Arabic (`ar`) | `PP-OCRv5_server_det` | `arabic_PP-OCRv5_mobile_rec` | Verified and cached |
| English (`en`) | `PP-OCRv6_medium_det` | `PP-OCRv6_medium_rec` | Verified and cached as PaddleOCR 3.7 default |

The official PaddleOCR 3.x model table identifies `arabic_PP-OCRv5_mobile_rec` as supporting Arabic and English, and lists `en_PP-OCRv5_mobile_rec` as the English PP-OCRv5 candidate. The current worker deliberately reports the model resolved by the installed pipeline. The explicit English PP-OCRv5 weight was not claimed as verified because its local PaddleX cache download was blocked by a Windows cache-lock permission error.

Model code/repository licensing is Apache-2.0. Before redistributing downloaded weights in an installer, retain upstream model notices and perform a separate model-weight redistribution review.

## Offline operation

The verified model directories are present under `C:\Users\PC\.paddlex\official_models`. With `PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK=True`, the worker loads cached weights and performs local CPU inference. This verifies cache-only startup; a full machine-wide network-disconnect test was not performed because changing Windows firewall state requires explicit administrative approval.

## Fallback policy

Fallback/mock engines remain available for deterministic tests and environments without the venv. They are never reported as successful production PaddleOCR: the worker health response includes `production_engine=false`, and the .NET health check reports the worker unhealthy for production when fallback is active.
