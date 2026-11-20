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

def _load_ocr_engine(preferred: str):
    from ocr.base import BaseOcrEngine
    from ocr.mock_engine import MockOcrEngine
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

def get_best_available_engine(preferred: str = "paddle") -> Any:
    return _load_ocr_engine(preferred)

class WorkerServer:
    def __init__(self):
        # Production is the default. Tests may explicitly opt into mock or
        # tesseract; a silent fallback must never look like PaddleOCR health.
        self.engine_name = os.environ.get("FILELENS_OCR_ENGINE", "paddle")
        self.role = os.environ.get("FILELENS_WORKER_ROLE", "ocr").lower()
        self.engine = get_best_available_engine(self.engine_name) if self.role == "ocr" else None
        self._siglip = None
        self.version = "0.3.0"
        self.is_production_engine = bool(self.engine and self.engine.get_version().startswith("PaddleOCR/"))
        
    def handle_request(self, req: Dict[str, Any]) -> Dict[str, Any]:
        cmd = req.get("command", "")
        req_id = req.get("id", "")
        
        if cmd == "ping":
            return {
                "id": req_id,
                "status": "success",
                "response": "pong",
                "version": self.version,
                "engine": self.engine.get_version() if self.engine else "SigLIP2VisualWorker",
                "role": self.role,
                "production_engine": self.is_production_engine
            }
            
        elif cmd == "health":
            return {
                "id": req_id,
                "status": "success",
                "is_healthy": True,
                "production_engine": self.is_production_engine,
                "version": self.version,
                "engine": self.engine.get_version() if self.engine else "SigLIP2VisualWorker",
                "role": self.role,
                "visual_provider": "SigLIP2VisualEmbeddingProvider" if self.role == "visual" else None,
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
                
            if self.engine is None:
                return {"id": req_id, "status": "error", "error": "OCR commands are unavailable in visual worker role"}
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
            if self.engine is None:
                return {"id": req_id, "status": "error", "error": "OCR commands are unavailable in visual worker role"}
            res = self.engine.extract_text(page_bytes, language=language, page_number=page_number)
            return {
                "id": req_id,
                "status": "success",
                "result": res.model_dump()
            }

        elif cmd == "image_embedding":
            # Legacy deterministic OpenCV descriptor retained for exact/near-duplicate tests.
            import numpy as np
            import cv2
            image_b64 = req.get("image_base64")
            if not image_b64:
                return {"id": req_id, "status": "error", "error": "Missing image payload"}
            raw = base64.b64decode(image_b64)
            image = cv2.imdecode(np.frombuffer(raw, dtype=np.uint8), cv2.IMREAD_COLOR)
            if image is None or image.size == 0:
                return {"id": req_id, "status": "error", "error": "Invalid image payload"}
            if image.shape[0] * image.shape[1] > 8192 * 8192:
                return {"id": req_id, "status": "error", "error": "Image dimensions exceed safety limit"}
            small = cv2.resize(image, (64, 64), interpolation=cv2.INTER_AREA)
            rgb = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
            gray = cv2.cvtColor(small, cv2.COLOR_BGR2GRAY)
            edges = cv2.Canny(gray, 80, 160)
            parts = []
            for channel in cv2.split(rgb):
                parts.extend((cv2.calcHist([channel], [0], None, [16], [0, 256]).ravel() / (64 * 64)).tolist())
            parts.extend((cv2.calcHist([gray], [0], None, [16], [0, 256]).ravel() / (64 * 64)).tolist())
            parts.extend((cv2.calcHist([edges], [0], None, [16], [0, 256]).ravel() / (64 * 64)).tolist())
            # Spatial color means/stds make the descriptor useful for scene and object retrieval.
            for block in (rgb[:32, :32], rgb[:32, 32:], rgb[32:, :32], rgb[32:, 32:]):
                parts.extend((block.mean(axis=(0, 1)) / 255.0).tolist())
                parts.extend((block.std(axis=(0, 1)) / 255.0).tolist())
            vector = np.asarray(parts, dtype=np.float32)
            vector /= max(float(np.linalg.norm(vector)), 1e-12)
            dct = cv2.dct(gray.astype(np.float32))[:8, :8]
            median = float(np.median(dct[1:, 1:]))
            phash = ''.join('1' if value >= median else '0' for value in dct.ravel())
            return {"id": req_id, "status": "success", "result": {
                "model_name": "FileLens-VisualFeature-CPU", "model_version": "1.0",
                "vector": vector.tolist(), "width": int(image.shape[1]), "height": int(image.shape[0]),
                "perceptual_hash": phash
            }}

        elif cmd in ("embed_image", "embed_batch"):
            if self.role != "visual":
                return {"id": req_id, "status": "error", "error": "SigLIP commands require the visual worker role"}
            if self._siglip is None:
                from visual.siglip_provider import SigLipProvider
                self._siglip = SigLipProvider()
            if cmd == "embed_image":
                result = self._siglip.embed_request(req)
                return {"id": req_id, "status": "success", "result": result}
            items = req.get("images")
            if not isinstance(items, list) or not items:
                return {"id": req_id, "status": "error", "error": "embed_batch requires a non-empty images array"}
            return {"id": req_id, "status": "success", "results": [self._siglip.embed_request(item) for item in items]}
            
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

