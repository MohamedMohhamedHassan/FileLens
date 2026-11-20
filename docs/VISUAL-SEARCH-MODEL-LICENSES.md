# Phase 4 Visual Model and License Review

## Evaluation

The existing Python 3.12 venv contains PaddleOCR/PaddlePaddle/OpenCV but does not contain Torch, TorchVision, ONNX Runtime, OpenCLIP, or Transformers. No CLIP/OpenCLIP weights were present locally, so no CLIP benchmark is claimed.

The obvious OpenAI CLIP ViT-B/32 candidate was not activated. Its model card describes research intent and explicitly lists deployed and unconstrained image-search use as out of scope without task-specific evaluation. The implementation library and pretrained weights must be reviewed separately; an implementation-library license is not evidence of a weight license. See the [OpenAI CLIP model card](https://huggingface.co/openai/clip-vit-base-patch32) and [official CLIP repository](https://github.com/openai/CLIP).

## Active provider

The active Phase 4 provider is `FileLens-VisualFeature-CPU` version `1.0`. It is a deterministic local OpenCV descriptor (RGB/grayscale/edge histograms plus spatial statistics and an 8×8 perceptual hash), not a learned semantic model. It has no model-weight license or external download dependency.

This provider supports offline exact/near-duplicate and low-level visual similarity validation. It must not be described as CLIP semantic similarity. A future learned model requires a separate license decision, pinned runtime, downloaded-weight verification, CPU benchmark, and offline test before activation.
