"""
PaddleOCR 3.x Engine Adapter for Arabic and English OCR.
"""
from __future__ import annotations
import time
import io
from typing import Optional, List
import numpy as np
from PIL import Image
from .base import BaseOcrEngine, OcrResult, OcrLine, OcrWord, BoundingBox
from .preprocessor import validate_and_load_image, preprocess_for_ocr

class PaddleOcrEngine(BaseOcrEngine):
    """
    Local OCR engine backed by PaddleOCR.
    Supports Arabic ('arabic') and English ('en') recognition.
    """
    
    def __init__(self, lang: str = "arabic", use_gpu: bool = False):
        self.lang = lang
        self.use_gpu = use_gpu
        self._ocr = None
        self.version = "3.0.0"
        
    def _init_model(self):
        if self._ocr is None:
            try:
                from paddleocr import PaddleOCR
                self._ocr = PaddleOCR(
                    use_angle_cls=True,
                    lang=self.lang,
                    use_gpu=self.use_gpu,
                    show_log=False
                )
            except ImportError as ex:
                raise RuntimeError(f"PaddleOCR is not installed in the environment: {ex}")
                
    def get_version(self) -> str:
        return f"PaddleOCR/{self.version} (lang={self.lang})"
        
    def extract_text(self, image_bytes: bytes, language: str = "auto", page_number: Optional[int] = None) -> OcrResult:
        start_time = time.perf_counter()
        self._init_model()
        
        img = validate_and_load_image(image_bytes)
        preprocessed = preprocess_for_ocr(img)
        img_np = np.array(preprocessed)
        
        results = self._ocr.ocr(img_np, cls=True)
        
        lines: List[OcrLine] = []
        raw_text_parts = []
        total_conf = 0.0
        word_count = 0
        
        img_w, img_h = img.size
        
        if results and len(results) > 0 and results[0]:
            for item in results[0]:
                poly, (text, conf) = item
                raw_text_parts.append(text)
                total_conf += conf
                word_count += 1
                
                # Convert 4-point polygon to normalized bounding box [x, y, w, h]
                xs = [p[0] for p in poly]
                ys = [p[1] for p in poly]
                min_x = max(0.0, min(xs)) / max(1, img_w)
                min_y = max(0.0, min(ys)) / max(1, img_h)
                box_w = (max(xs) - min(xs)) / max(1, img_w)
                box_h = (max(ys) - min(ys)) / max(1, img_h)
                
                bbox = BoundingBox(x=min_x, y=min_y, width=box_w, height=box_h)
                lines.append(OcrLine(text=text, confidence=float(conf), bbox=bbox))
                
        full_text = "\n".join(raw_text_parts)
        avg_conf = (total_conf / word_count) if word_count > 0 else 0.0
        elapsed_ms = int((time.perf_counter() - start_time) * 1000)
        
        return OcrResult(
            success=True,
            engine="PaddleOCR",
            model_name="ch_PP-OCRv4_multilingual",
            model_version=self.version,
            language=self.lang,
            raw_text=full_text,
            normalized_text=full_text.lower(),
            confidence=avg_conf,
            lines=lines,
            processing_time_ms=elapsed_ms,
            page_number=page_number
        )

