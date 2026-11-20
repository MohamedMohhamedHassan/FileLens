from __future__ import annotations

import base64
import io
import os
from pathlib import Path

import torch
from PIL import Image, UnidentifiedImageError
from transformers import AutoModel, AutoProcessor


class SigLipProvider:
    PROVIDER = "SigLIP2VisualEmbeddingProvider"
    MODEL = "google/siglip2-base-patch16-224"
    REVISION = "0ad8c6e0ff16615356a08a1ad8c8bbc8930c434e"
    VERSION = REVISION
    PREPROCESSING = "AutoProcessor local RGB 224x224; model-native normalization; L2 normalized image features"
    DIMENSION = 768
    MAX_BYTES = 100 * 1024 * 1024

    def __init__(self) -> None:
        model_dir = os.environ.get("FILELENS_SIGLIP_MODEL", r"D:\FileLens-AI\models\siglip2-base-patch16-224")
        self.model_dir = Path(model_dir)
        if not self.model_dir.is_dir():
            raise FileNotFoundError(f"SigLIP model directory does not exist: {self.model_dir}")
        self.processor = AutoProcessor.from_pretrained(self.model_dir, local_files_only=True, trust_remote_code=False)
        self.model = AutoModel.from_pretrained(self.model_dir, local_files_only=True, trust_remote_code=False)
        self.model.eval().to("cpu")

    def _read_image(self, request: dict) -> Image.Image:
        if request.get("image_base64"):
            raw = base64.b64decode(request["image_base64"], validate=True)
        elif request.get("image_path"):
            path = Path(request["image_path"])
            if not path.is_file():
                raise FileNotFoundError(f"Image path does not exist: {path}")
            raw = path.read_bytes()
        else:
            raise ValueError("Missing image_base64 or image_path")
        if len(raw) > self.MAX_BYTES:
            raise ValueError("Image exceeds the 100 MB safety limit")
        try:
            with Image.open(io.BytesIO(raw)) as image:
                image.load()
                return image.convert("RGB")
        except (UnidentifiedImageError, OSError) as ex:
            raise ValueError(f"Invalid or unsupported image: {ex}") from ex

    def embed_request(self, request: dict) -> dict:
        image = self._read_image(request)
        width, height = image.size
        with torch.inference_mode():
            inputs = self.processor(images=image, return_tensors="pt")
            features = self.model.get_image_features(**inputs).float()
            features = torch.nn.functional.normalize(features, p=2, dim=-1)
        vector = features[0].tolist()
        if len(vector) != self.DIMENSION:
            raise RuntimeError(f"Unexpected SigLIP embedding dimension: {len(vector)}; expected {self.DIMENSION}")
        return {
            "provider_name": self.PROVIDER,
            "model_name": self.MODEL,
            "model_version": self.VERSION,
            "model_revision": self.REVISION,
            "preprocessing": self.PREPROCESSING,
            "dimension": self.DIMENSION,
            "vector": vector,
            "width": width,
            "height": height,
            "perceptual_hash": "",
        }
