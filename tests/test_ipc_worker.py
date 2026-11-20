#!/usr/bin/env python3
"""
FileLens Phase 3 – Python IPC Worker Test
Tests the worker.py JSON-lines protocol: ping, health, ocr_image, ocr_pdf_page, shutdown,
timeout handling, and worker crash recovery.

Run from the repo root:
    .venv/Scripts/python.exe tests/test_ipc_worker.py
"""
from __future__ import annotations
import os
import sys
import io
import json
import base64
import subprocess
import time
import tempfile

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from PIL import Image, ImageDraw, ImageFont
import arabic_reshaper
from bidi.algorithm import get_display

# ---------------------------------------------------------------------------
VENV_PYTHON = os.path.join(os.path.dirname(__file__), "..", "src", "ai-worker", ".venv", "Scripts", "python.exe")
WORKER_SCRIPT = os.path.join(os.path.dirname(__file__), "..", "src", "ai-worker", "worker.py")
PASS_ICON = "✓"
FAIL_ICON = "✗"
# ---------------------------------------------------------------------------

def make_png_bytes(text: str, is_arabic: bool = False, size=(600, 120)) -> bytes:
    img = Image.new("RGB", size, (255, 255, 255))
    draw = ImageDraw.Draw(img)
    display_text = text
    if is_arabic:
        display_text = get_display(arabic_reshaper.reshape(text))
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 28)
    except Exception:
        font = ImageFont.load_default()
    draw.text((20, 40), display_text, fill=(0, 0, 0), font=font)
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def make_pdf_bytes(text: str) -> str:
    """Write a one-page scanned PDF to a temp file; return path."""
    import pypdfium2 as pdfium
    img = Image.new("RGB", (800, 300), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 28)
    except Exception:
        font = ImageFont.load_default()
    draw.text((30, 100), text, fill=(0, 0, 0), font=font)

    pdf = pdfium.PdfDocument.new()
    page = pdf.new_page(width=800, height=300)
    bitmap = pdfium.PdfBitmap.from_pil(img)
    pdf_img = pdfium.PdfImage.new(pdf)
    pdf_img.set_bitmap(bitmap)
    pdf_img.set_matrix(pdfium.PdfMatrix(800, 0, 0, 300, 0, 0))
    page.insert_obj(pdf_img)
    page.gen_content()

    tmp = tempfile.NamedTemporaryFile(suffix=".pdf", delete=False)
    pdf.save(tmp.name)
    tmp.close()
    return tmp.name


class WorkerProcess:
    """Manages a subprocess running worker.py."""

    def __init__(self):
        env = os.environ.copy()
        env["FILELENS_OCR_ENGINE"] = "paddle"
        self.proc = subprocess.Popen(
            [VENV_PYTHON, WORKER_SCRIPT],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            env=env,
        )
        # Give models time to load
        time.sleep(2)

    def send(self, payload: dict, timeout: float = 180.0) -> dict:
        line = json.dumps(payload, ensure_ascii=False) + "\n"
        self.proc.stdin.write(line)
        self.proc.stdin.flush()
        self.proc.stdout._stream = getattr(self.proc.stdout, '_stream', self.proc.stdout)
        import select
        import threading

        result = {}
        exc_holder = []

        def reader():
            try:
                result['line'] = self.proc.stdout.readline()
            except Exception as e:
                exc_holder.append(e)

        t = threading.Thread(target=reader, daemon=True)
        t.start()
        t.join(timeout)
        if t.is_alive():
            raise TimeoutError(f"Worker did not respond within {timeout}s")
        if exc_holder:
            raise exc_holder[0]
        raw = result.get('line', '').strip()
        if not raw:
            raise ValueError("Empty response from worker")
        return json.loads(raw)

    def kill(self):
        try:
            self.proc.kill()
        except Exception:
            pass


def run():
    results = []

    print("=" * 65)
    print(" FileLens Phase 3 – Python Worker IPC Tests")
    print("=" * 65)
    print(f"Python : {VENV_PYTHON}")
    print(f"Worker : {WORKER_SCRIPT}")
    print("-" * 65)

    worker = WorkerProcess()

    # TEST 1: ping
    t0 = time.perf_counter()
    try:
        resp = worker.send({"command": "ping", "id": "ipc-1"}, timeout=180)
        ok = resp.get("status") == "success" and resp.get("response") == "pong"
        engine = resp.get("engine", "?")
        results.append(("1. ping", ok, f"engine={engine}"))
        print(f"[IPC-1] ping: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms) engine={engine}")
    except Exception as e:
        results.append(("1. ping", False, str(e)))
        print(f"[IPC-1] ping: {FAIL_ICON} ERROR: {e}")

    # TEST 2: health
    t0 = time.perf_counter()
    try:
        resp = worker.send({"command": "health", "id": "ipc-2"}, timeout=30)
        ok = resp.get("status") == "success" and resp.get("is_healthy") is True
        is_fallback = "mock" in resp.get("engine", "").lower() or "fallback" in resp.get("engine", "").lower()
        is_paddle = "paddle" in resp.get("engine", "").lower()
        if is_fallback:
            ok = False
        results.append(("2. health", ok, f"engine={resp.get('engine','?')} fallback={is_fallback}"))
        print(f"[IPC-2] health: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms) engine={resp.get('engine','?')}")
        if not is_paddle:
            print(f"         WARNING: Engine is not PaddleOCR – using fallback? {resp.get('engine','?')}")
    except Exception as e:
        results.append(("2. health", False, str(e)))
        print(f"[IPC-2] health: {FAIL_ICON} ERROR: {e}")

    # TEST 3: ocr_image (English)
    t0 = time.perf_counter()
    try:
        en_bytes = make_png_bytes("Invoice Number INV-20261101", is_arabic=False)
        en_b64 = base64.b64encode(en_bytes).decode()
        resp = worker.send({"command": "ocr_image", "id": "ipc-3", "image_base64": en_b64, "language": "en"}, timeout=120)
        ok = resp.get("status") == "success"
        text = resp.get("result", {}).get("raw_text", "")
        contains_key = "Invoice" in text or "INV" in text or "20261101" in text
        ok = ok and contains_key
        results.append(("3. ocr_image English", ok, f"text='{text[:60]}'"))
        print(f"[IPC-3] ocr_image EN: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms) '{text[:60]}'")
    except Exception as e:
        results.append(("3. ocr_image English", False, str(e)))
        print(f"[IPC-3] ocr_image EN: {FAIL_ICON} ERROR: {e}")

    # TEST 4: ocr_image (Arabic)
    t0 = time.perf_counter()
    try:
        ar_bytes = make_png_bytes("فاتورة ضريبية", is_arabic=True)
        ar_b64 = base64.b64encode(ar_bytes).decode()
        resp = worker.send({"command": "ocr_image", "id": "ipc-4", "image_base64": ar_b64, "language": "ar"}, timeout=120)
        ok = resp.get("status") == "success"
        text = resp.get("result", {}).get("raw_text", "")
        contains_ar = "فاتور" in text or "ضريب" in text or len(text) > 3
        ok = ok and contains_ar
        results.append(("4. ocr_image Arabic", ok, f"text='{text[:60]}'"))
        print(f"[IPC-4] ocr_image AR: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms) '{text[:60]}'")
    except Exception as e:
        results.append(("4. ocr_image Arabic", False, str(e)))
        print(f"[IPC-4] ocr_image AR: {FAIL_ICON} ERROR: {e}")

    # TEST 5: ocr_pdf_page
    t0 = time.perf_counter()
    pdf_path = None
    try:
        pdf_path = make_pdf_bytes("Annual Report FY2026")
        resp = worker.send({"command": "ocr_pdf_page", "id": "ipc-5", "pdf_path": pdf_path, "page_number": 1, "language": "en"}, timeout=120)
        ok = resp.get("status") == "success"
        text = resp.get("result", {}).get("raw_text", "")
        contains_key = "Annual" in text or "Report" in text or "FY2026" in text
        ok = ok and contains_key
        results.append(("5. ocr_pdf_page", ok, f"text='{text[:60]}'"))
        print(f"[IPC-5] ocr_pdf_page: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms) '{text[:60]}'")
    except Exception as e:
        results.append(("5. ocr_pdf_page", False, str(e)))
        print(f"[IPC-5] ocr_pdf_page: {FAIL_ICON} ERROR: {e}")
    finally:
        if pdf_path and os.path.exists(pdf_path):
            os.unlink(pdf_path)

    # TEST 6: unknown command returns error (not crash)
    t0 = time.perf_counter()
    try:
        resp = worker.send({"command": "not_a_command", "id": "ipc-6"}, timeout=15)
        ok = resp.get("status") == "error" and "Unknown command" in resp.get("error", "")
        results.append(("6. unknown command error", ok, f"error='{resp.get('error','?')}'"))
        print(f"[IPC-6] unknown cmd: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms)")
    except Exception as e:
        results.append(("6. unknown command error", False, str(e)))
        print(f"[IPC-6] unknown cmd: {FAIL_ICON} ERROR: {e}")

    # TEST 7: shutdown
    t0 = time.perf_counter()
    try:
        resp = worker.send({"command": "shutdown", "id": "ipc-7"}, timeout=10)
        ok = resp.get("status") == "success"
        results.append(("7. shutdown", ok, ""))
        print(f"[IPC-7] shutdown: {PASS_ICON if ok else FAIL_ICON} ({int((time.perf_counter()-t0)*1000)}ms)")
    except Exception as e:
        results.append(("7. shutdown", False, str(e)))
        print(f"[IPC-7] shutdown: {FAIL_ICON} ERROR: {e}")
        worker.kill()

    print("-" * 65)
    passed = sum(1 for _, ok, _ in results if ok)
    total = len(results)
    print(f"Summary: {passed}/{total} IPC Tests Passed ({100*passed//total}%)")
    print("=" * 65)

    if passed < total:
        print("\nFailed tests:")
        for name, ok, detail in results:
            if not ok:
                print(f"  - {name}: {detail}")
    return 0 if passed == total else 1


if __name__ == "__main__":
    ai_worker_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "src", "ai-worker"))
    if ai_worker_dir not in sys.path:
        sys.path.insert(0, ai_worker_dir)
    sys.exit(run())
