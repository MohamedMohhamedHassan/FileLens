# FileLens Phase 3 OCR Verification Report

Verification date: 2026-11-18. All production rows below used the real PaddleOCR worker, never `FallbackOcrEngine` or `MockOcrEngine`.

## Runtime

- Python 3.12.10 x64; Windows 11 x64.
- PaddlePaddle 3.3.1 CPU; PaddleOCR 3.7.0; PaddleX 3.7.2.
- Arabic: `PP-OCRv5_server_det` + `arabic_PP-OCRv5_mobile_rec`.
- English: `PP-OCRv6_medium_det` + `PP-OCRv6_medium_rec` (the installed 3.7 default).

## Ground-truth OCR results

| Test | CER | WER | Confidence | Time | Result |
|---|---:|---:|---:|---:|---|
| Arabic image | 17.4% | 50.0% | 0.9075 | 20,546 ms | PASS |
| English image | 0.0% | 0.0% | 1.0000 | 13,657 ms | PASS |
| Mixed Arabic/English invoice | 1.8% | 9.1% | 0.9965 | 19,122 ms | PASS |
| Arabic diacritics | 22.2% | 33.3% | 0.9367 | 10,069 ms | PASS |
| Rotated image | 0.0% | 0.0% | 1.0000 | 9,014 ms | PASS |
| Low-resolution screenshot | 2.9% | 25.0% | 0.9973 | 3,254 ms | PASS |
| Scanned PDF page | 0.0% | 0.0% | 0.9996 | 33,414 ms | PASS |
| Two-page scanned PDF | 4.0% average | 15.0% recorded | 0.9913 | 157,707 ms | PASS |

Summary: **8/8 scenarios passed**. Full recognized text and ground truth are in `tests/production_ocr_verification_report.json`.

Arabic errors are visible in long words and diacritics; search normalization remains separate from raw OCR. These synthetic results are not a universal accuracy guarantee.

## IPC and recovery

`tests/test_ipc_worker.py`: **7/7 passed** — ping, health, English image, Arabic image, scanned PDF page, unknown-command error, and shutdown. Health reported `PaddleOCR/3.7.0` and `production_engine=true`. The client has bounded request timeouts and restarts the worker after timeout/communication failure.

## Runtime checks

- `import paddle`, `paddle.__version__`, OpenCV and pypdfium2: PASS.
- `paddle.utils.run_check()`: PASS on one CPU.
- Native Paddle libraries: loaded during inference; CPU inference completed.
- Cached local model inference with source-check disabled: PASS.
- Full Windows network-disconnect/firewall test: not run; no firewall state was changed.

## Regression evidence

Verified on 2026-11-18:

```powershell
dotnet build FileLens.sln -c Release
dotnet run --project tests/FileLens.UnitTests -c Release
dotnet run --project tests/FileLens.IntegrationTests -c Release
& .\src\ai-worker\.venv\Scripts\python.exe .\tests\test_ipc_worker.py
& .\src\ai-worker\.venv\Scripts\python.exe .\tests\verify_production_ocr.py
```

- Release solution build: **0 warnings, 0 errors**.
- Unit tests: **80/80 passed, 0 failed**.
- Integration tests: **19/19 passed, 0 failed**.
- Production PaddleOCR scenarios: **8/8 passed, 0 failed**.
- Production worker IPC checks: **7/7 passed, 0 failed**.
- Fallback unit/integration tests and production PaddleOCR tests remain reported separately.
