#!/usr/bin/env python3
"""
FileLens Local AI Worker Process
Communicates with FileLens .NET Host via JSON-lines over stdin/stdout.
"""
from __future__ import annotations
import sys
import os
import json
import base64
import traceback
from typing import Dict, Any, Optional

from ocr.base import BaseOcrEngine, OcrResult
from ocr.mock_engine import MockOcrEngine

def get_best_available_engine(preferred: str = "paddle") -> BaseOcrEngine:
    if preferred == "mock":
        return MockOcrEngine()
        
    if preferred == "paddle":
        try:
            from ocr.paddle_engine import PaddleOcrEngine
            return PaddleOcrEngine()
        except Exception:
            pass
            
    if preferred == "tesseract":
        try:
            from ocr.tesseract_engine import TesseractOcrEngine
            return TesseractOcrEngine()
        except Exception:
            pass
            
    return MockOcrEngine()

class WorkerServer:
    def __init__(self):
        self.engine_name = os.environ.get("FILELENS_OCR_ENGINE", "auto")
        self.engine = get_best_available_engine(self.engine_name)
        self.version = "0.3.0"
        
    def handle_request(self, req: Dict[str, Any]) -> Dict[str, Any]:
        cmd = req.get("command", "")
        req_id = req.get("id", "")
        
        if cmd == "ping":
            return {
                "id": req_id,
                "status": "success",
                "response": "pong",
                "version": self.version,
                "engine": self.engine.get_version()
            }
            
        elif cmd == "health":
            return {
                "id": req_id,
                "status": "success",
                "is_healthy": True,
                "version": self.version,
                "engine": self.engine.get_version(),
                "pid": os.getpid()
            }
            
        elif cmd == "ocr_image":
            image_b64 = req.get("image_base64")
            image_path = req.get("image_path")
            language = req.get("language", "auto")
            
            image_bytes = None
            if image_b64:
                image_bytes = base64.b64decode(image_b64)
            elif image_path and os.path.exists(image_path):
                with open(image_path, "rb") as f:
                    image_bytes = f.read()
            else:
                return {"id": req_id, "status": "error", "error": "Missing valid image payload or path"}
                
            res = self.engine.extract_text(image_bytes, language=language)
            return {
                "id": req_id,
                "status": "success",
                "result": res.model_dump()
            }
            
        elif cmd == "ocr_pdf_page":
            pdf_path = req.get("pdf_path")
            page_number = req.get("page_number", 1)
            language = req.get("language", "auto")
            
            if not pdf_path or not os.path.exists(pdf_path):
                return {"id": req_id, "status": "error", "error": f"PDF path does not exist: {pdf_path}"}
                
            from pdf.renderer import render_pdf_page_to_bytes
            with open(pdf_path, "rb") as f:
                pdf_bytes = f.read()
                
            page_bytes, w, h = render_pdf_page_to_bytes(pdf_bytes, page_number)
            res = self.engine.extract_text(page_bytes, language=language, page_number=page_number)
            return {
                "id": req_id,
                "status": "success",
                "result": res.model_dump()
            }
            
        elif cmd == "shutdown":
            return {"id": req_id, "status": "success", "response": "shutting down"}
            
        else:
            return {"id": req_id, "status": "error", "error": f"Unknown command: {cmd}"}

    def run(self):
        # Read JSON lines from stdin and write JSON lines to stdout
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                req = json.loads(line)
                resp = self.handle_request(req)
                sys.stdout.write(json.dumps(resp, ensure_ascii=False) + "\n")
                sys.stdout.flush()
                if req.get("command") == "shutdown":
                    break
            except Exception as ex:
                err_resp = {
                    "id": "",
                    "status": "error",
                    "error": str(ex),
                    "traceback": traceback.format_exc()
                }
                sys.stdout.write(json.dumps(err_resp) + "\n")
                sys.stdout.flush()

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        server = WorkerServer()
        resp = server.handle_request({"command": "health", "id": "test-1"})
        print(json.dumps(resp, indent=2))
    else:
        server = WorkerServer()
        server.run()

