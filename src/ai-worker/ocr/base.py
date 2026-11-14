"""
Base models and abstract class for FileLens OCR engines.
"""
from __future__ import annotations
from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field
import abc

class BoundingBox(BaseModel):
    x: float = Field(..., description="Left coordinate (0.0 to 1.0 or pixels)")
    y: float = Field(..., description="Top coordinate (0.0 to 1.0 or pixels)")
    width: float = Field(..., description="Width")
    height: float = Field(..., description="Height")

class OcrWord(BaseModel):
    text: str
    confidence: float
    bbox: BoundingBox

class OcrLine(BaseModel):
    text: str
    confidence: float
    bbox: BoundingBox
    words: List[OcrWord] = Field(default_factory=list)

class OcrResult(BaseModel):
    success: bool
    engine: str
    model_name: str
    model_version: str
    language: str
    raw_text: str
    normalized_text: str
    confidence: float
    lines: List[OcrLine] = Field(default_factory=list)
    processing_time_ms: int = 0
    page_number: Optional[int] = None
    error_message: Optional[str] = None
    metadata: Dict[str, Any] = Field(default_factory=dict)

class BaseOcrEngine(abc.ABC):
    """Abstract interface for local OCR engines."""
    
    @abc.abstractmethod
    def extract_text(self, image_bytes: bytes, language: str = "auto", page_number: Optional[int] = None) -> OcrResult:
        """Extract text from raw image bytes."""
        pass
    
    @abc.abstractmethod
    def get_version(self) -> str:
        """Return engine name and version."""
        pass

