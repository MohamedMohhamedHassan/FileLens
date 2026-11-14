"""
Deterministic mock OCR engine for offline testing and baseline evaluation.
"""
from __future__ import annotations
import time
from typing import Optional, List
from .base import BaseOcrEngine, OcrResult, OcrLine, OcrWord, BoundingBox

class MockOcrEngine(BaseOcrEngine):
    """
    Simulated OCR engine used in automated test suites and fallback offline validation.
    Extracts text based on image dimensions, metadata, or embedded synthetic test patterns.
    """
    
    def __init__(self, model_version: str = "3.0.0-mock"):
        self.model_version = model_version
        
    def get_version(self) -> str:
        return f"MockOcrEngine/{self.model_version}"
        
    def extract_text(self, image_bytes: bytes, language: str = "auto", page_number: Optional[int] = None) -> OcrResult:
        start_time = time.perf_counter()
        
        # Check for embedded synthetic ground-truth metadata in bytes
        raw_text = ""
        try:
            # Check for utf-8 text payload or synthetic marker
            text_candidate = image_bytes.decode('utf-8', errors='ignore')
            if "[OCR_GROUND_TRUTH:" in text_candidate:
                start_idx = text_candidate.find("[OCR_GROUND_TRUTH:") + len("[OCR_GROUND_TRUTH:")
                end_idx = text_candidate.find("]", start_idx)
                if end_idx != -1:
                    raw_text = text_candidate[start_idx:end_idx].strip()
        except Exception:
            pass
            
        if not raw_text:
            if language == "ar":
                raw_text = "تقرير المبيعات السنوي لعام ٢٠٢٦"
            elif language == "mixed":
                raw_text = "Enterprise Architecture تقرير الأنظمة 2026"
            else:
                raw_text = "FileLens Optical Character Recognition Engine"
                
        # Generate bounding boxes and lines
        lines: List[OcrLine] = []
        words = raw_text.split()
        
        line_bbox = BoundingBox(x=0.05, y=0.1, width=0.9, height=0.08)
        word_objs = []
        x_step = 0.85 / max(1, len(words))
        for i, w in enumerate(words):
            word_objs.append(OcrWord(
                text=w,
                confidence=0.95,
                bbox=BoundingBox(x=0.05 + i * x_step, y=0.1, width=x_step * 0.9, height=0.08)
            ))
            
        lines.append(OcrLine(
            text=raw_text,
            confidence=0.95,
            bbox=line_bbox,
            words=word_objs
        ))
        
        elapsed_ms = int((time.perf_counter() - start_time) * 1000)
        
        return OcrResult(
            success=True,
            engine="MockOcrEngine",
            model_name="mock-bilingual",
            model_version=self.model_version,
            language=language if language != "auto" else "mixed",
            raw_text=raw_text,
            normalized_text=raw_text.lower(),
            confidence=0.95,
            lines=lines,
            processing_time_ms=elapsed_ms,
            page_number=page_number
        )

