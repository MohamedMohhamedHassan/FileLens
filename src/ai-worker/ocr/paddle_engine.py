"""
PaddleOCR 3.x Engine Adapter for Arabic and English OCR.
Uses PaddleOCR 3.7+ with PaddlePaddle 3.x, PP-OCRv5 Arabic recognition,
and PP-OCRv5 server detection.
"""
from __future__ import annotations
import os
import time
import io
from typing import Optional, List, Dict, Any
import numpy as np
from PIL import Image

# Ensure local execution without remote source checks
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
os.environ.setdefault("FLAGS_use_onednn", "0")

# CPU compatibility runner patch for Windows PIR instruction execution
try:
    import paddlex.inference.models.runners.paddle_static.runner as runner_mod
    if not hasattr(runner_mod, "_filelens_patched"):
        _orig_create = runner_mod.PaddleStaticRunner._create
        def _patched_create(self):
            self._config["run_mode"] = "paddle"
            return _orig_create(self)
        runner_mod.PaddleStaticRunner._create = _patched_create
        runner_mod._filelens_patched = True
except Exception:
    pass

from .base import BaseOcrEngine, OcrResult, OcrLine, OcrWord, BoundingBox
from .preprocessor import validate_and_load_image, preprocess_for_ocr

class PaddleOcrEngine(BaseOcrEngine):
    """
    Local OCR engine backed by PaddleOCR 3.7+ / PP-OCRv5.
    Supports Arabic ('ar') and English ('en') recognition.
    """
    
    def __init__(self, lang: str = "ar", use_gpu: bool = False):
        self.lang = "ar" if lang in ("ar", "arabic", "auto") else lang
        self.use_gpu = use_gpu
        self._models: Dict[str, Any] = {}
        self.version = "3.7.0"
        self.det_model = "PP-OCRv5_server_det"
        self.rec_model = "arabic_PP-OCRv5_mobile_rec"
        self.en_det_model = "PP-OCRv6_medium_det"
        self.en_rec_model = "PP-OCRv6_medium_rec"

    def _get_model(self, lang: str):
        target_lang = "ar" if lang in ("ar", "arabic", "auto") else "en"
        if target_lang not in self._models:
            from paddleocr import PaddleOCR
            # PaddleOCR 3.7 resolves Arabic to the PP-OCRv5 Arabic model and
            # English to the PP-OCRv6 English default. Keep these names
            # explicit in the result instead of reporting an assumed model.
            self._models[target_lang] = PaddleOCR(lang=target_lang, device="cpu")
        return self._models[target_lang], target_lang

    def get_version(self) -> str:
        return f"PaddleOCR/{self.version} (rec={self.rec_model}, det={self.det_model})"
        
    def extract_text(self, image_bytes: bytes, language: str = "auto", page_number: Optional[int] = None) -> OcrResult:
        start_time = time.perf_counter()

        req_lang = language if language != "auto" else self.lang
        ocr_model, resolved_lang = self._get_model(req_lang)
        
        img = validate_and_load_image(image_bytes)
        if img.mode != "RGB":
            img = img.convert("RGB")
        img_np = np.array(img)
        img_w, img_h = img.size
        
        # Run inference using predict (PaddleOCR 3.x native pipeline)
        predictions = list(ocr_model.predict(img_np))
        
        lines: List[OcrLine] = []
        raw_text_parts: List[str] = []
        total_conf = 0.0
        word_count = 0
        
        for item in predictions:
            if not isinstance(item, dict):
                continue
            rec_texts = item.get("rec_texts", [])
            rec_scores = item.get("rec_scores", [])
            rec_polys = item.get("rec_polys", [])

            for idx, text in enumerate(rec_texts):
                if not text or not text.strip():
                    continue
                conf = float(rec_scores[idx]) if idx < len(rec_scores) else 0.85
                raw_text_parts.append(text)
                total_conf += conf
                word_count += 1
                
                # Bounding box
                min_x, min_y, box_w, box_h = 0.0, 0.0, 1.0, 0.1
                if idx < len(rec_polys):
                    poly = rec_polys[idx]
                    try:
                        xs = [float(p[0]) for p in poly]
                        ys = [float(p[1]) for p in poly]
                        min_x = max(0.0, min(xs)) / max(1, img_w)
                        min_y = max(0.0, min(ys)) / max(1, img_h)
                        box_w = min(1.0, (max(xs) - min(xs)) / max(1, img_w))
                        box_h = min(1.0, (max(ys) - min(ys)) / max(1, img_h))
                    except Exception:
                        pass
                
                bbox = BoundingBox(x=min_x, y=min_y, width=box_w, height=box_h)
                lines.append(OcrLine(text=text, confidence=conf, bbox=bbox))
                
        full_text = "\n".join(raw_text_parts)
        avg_conf = (total_conf / word_count) if word_count > 0 else 0.0
        elapsed_ms = int((time.perf_counter() - start_time) * 1000)

        model_name = self.rec_model if resolved_lang == "ar" else self.en_rec_model
        detection_model = self.det_model if resolved_lang == "ar" else self.en_det_model
        
        return OcrResult(
            success=True,
            engine="PaddleOCR",
            model_name=model_name,
            model_version="PP-OCRv5/PP-OCRv6",
            language=resolved_lang,
            raw_text=full_text,
            normalized_text=full_text.lower(),
            confidence=avg_conf,
            lines=lines,
            processing_time_ms=elapsed_ms,
            page_number=page_number,
            metadata={"detection_model": detection_model, "device": "cpu"}
        )
