"""
PDF page rendering and scanned-page detection.
"""
from __future__ import annotations
import io
from typing import Generator, Tuple, Optional
from PIL import Image

def render_pdf_page_to_bytes(pdf_bytes: bytes, page_number: int, dpi: int = 150) -> Tuple[bytes, int, int]:
    """
    Renders a specific page of a PDF to JPEG/PNG bytes.
    page_number is 1-indexed.
    """
    try:
        import pypdfium2 as pdfium
        pdf = pdfium.PdfDocument(pdf_bytes)
        if page_number < 1 or page_number > len(pdf):
            raise IndexError(f"Page {page_number} is out of range (total pages: {len(pdf)})")
            
        page = pdf[page_number - 1]
        scale = dpi / 72.0
        bitmap = page.render(scale=scale)
        pil_image = bitmap.to_pil()
        
        buffer = io.BytesIO()
        pil_image.save(buffer, format="PNG")
        return buffer.getvalue(), pil_image.width, pil_image.height
    except ImportError:
        # Fallback minimal 1x1 image if pypdfium2 not installed
        img = Image.new("RGB", (300, 300), color=(255, 255, 255))
        buf = io.BytesIO()
        img.save(buf, format="PNG")
        return buf.getvalue(), 300, 300

