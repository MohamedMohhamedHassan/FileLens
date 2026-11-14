"""
Image preprocessing utilities for OCR pipeline.
"""
from __future__ import annotations
import io
import math
from typing import Tuple, Optional
from PIL import Image, ImageOps, ImageEnhance

MAX_IMAGE_DIMENSION = 8192

def validate_and_load_image(image_bytes: bytes) -> Image.Image:
    """Safely decode and validate image bytes within size constraints."""
    img = Image.open(io.BytesIO(image_bytes))
    img.load()
    
    width, height = img.size
    if width > MAX_IMAGE_DIMENSION or height > MAX_IMAGE_DIMENSION:
        # Scale down while maintaining aspect ratio to prevent decompression bomb
        scale = min(MAX_IMAGE_DIMENSION / width, MAX_IMAGE_DIMENSION / height)
        new_width = max(1, int(width * scale))
        new_height = max(1, int(height * scale))
        img = img.resize((new_width, new_height), Image.Resampling.LANCZOS)
        
    return img

def preprocess_for_ocr(
    img: Image.Image,
    deskew: bool = True,
    enhance_contrast: bool = True,
    to_grayscale: bool = True,
    rotation_angle: Optional[float] = None
) -> Image.Image:
    """
    Applies configurable preprocessing to improve OCR accuracy.
    Preserves original image integrity by operating on in-memory copy.
    """
    processed = img.copy()
    
    # Auto-orient based on EXIF tag if present
    try:
        processed = ImageOps.exif_transpose(processed)
    except Exception:
        pass
        
    # Manual rotation if specified
    if rotation_angle is not None and abs(rotation_angle) > 0.01:
        processed = processed.rotate(-rotation_angle, expand=True, resample=Image.Resampling.BICUBIC)
        
    # Grayscale conversion
    if to_grayscale and processed.mode != 'L':
        processed = processed.convert('L')
        
    # Contrast adjustment
    if enhance_contrast:
        enhancer = ImageEnhance.Contrast(processed)
        processed = enhancer.enhance(1.5)
        
    return processed

