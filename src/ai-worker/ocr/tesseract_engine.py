"""
Tesseract OCR Engine Adapter for Arabic and English OCR.
"""
from __future__ import annotations
import time
from typing import Optional, List
from .base import BaseOcrEngine, OcrResult, OcrLine, OcrWord, BoundingBox
from .preprocessor import validate_and_load_image, preprocess_for_ocr

class TesseractOcrEngine(BaseOcrEngine):
    """
    Local OCR engine backed by Tesseract 5.x via pytesseract.
    Supports Arabic ('ara') and English ('eng').
    """
    
    def __init__(self, lang: str = "ara+eng"):
        self.lang = lang
        self.version = "5.3.0"
        
    def get_version(self) -> str:
        return f"Tesseract/{self.version} (lang={self.lang})"
        
    def extract_text(self, image_bytes: bytes, language: str = "auto", page_number: Optional[int] = None) -> OcrResult:
        start_time = time.perf_counter()
        
        try:
            import pytesseract
        except ImportError as ex:
            raise RuntimeError(f"pytesseract is not installed: {ex}")
            
        img = validate_and_load_image(image_bytes)
        preprocessed = preprocess_for_ocr(img)
        
        target_lang = self.lang
        if language == "ar":
            target_lang = "ara"
        elif language == "en":
            target_lang = "eng"
            
        data = pytesseract.image_to_data(preprocessed, lang=target_lang, output_type=pytesseract.Output.DICT)
        
        lines: List[OcrLine] = []
        raw_words = []
        total_conf = 0.0
        conf_count = 0
        img_w, img_h = img.size
        
        n_boxes = len(data['text'])
        current_line_words = []
        current_line_text = []
        
        for i in range(n_boxes):
            word_text = data['text'][i].strip()
            conf_val = float(data['conf'][i])
            if not word_text or conf_val < 0:
                continue
                
            raw_words.append(word_text)
            total_conf += conf_val
            conf_count += 1
            
            x = float(data['left'][i]) / max(1, img_w)
            y = float(data['top'][i]) / max(1, img_h)
            w = float(data['width'][i]) / max(1, img_w)
            h = float(data['height'][i]) / max(1, img_h)
            
            bbox = BoundingBox(x=x, y=y, width=w, height=h)
            current_line_words.append(OcrWord(text=word_text, confidence=conf_val / 100.0, bbox=bbox))
            current_line_text.append(word_text)
            
        full_text = " ".join(raw_words)
        avg_conf = (total_conf / (conf_count * 100.0)) if conf_count > 0 else 0.0
        elapsed_ms = int((time.perf_counter() - start_time) * 1000)
        
        if current_line_words:
            lines.append(OcrLine(
                text=" ".join(current_line_text),
                confidence=avg_conf,
                bbox=BoundingBox(x=0.0, y=0.0, width=1.0, height=1.0),
                words=current_line_words
            ))
            
        return OcrResult(
            success=True,
            engine="Tesseract",
            model_name="lstm-tessdata",
            model_version=self.version,
            language=target_lang,
            raw_text=full_text,
            normalized_text=full_text.lower(),
            confidence=avg_conf,
            lines=lines,
            processing_time_ms=elapsed_ms,
            page_number=page_number
        )

