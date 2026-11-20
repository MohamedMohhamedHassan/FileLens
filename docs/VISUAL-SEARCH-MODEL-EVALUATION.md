# Phase 4 Semantic Visual Model Evaluation

## Decision status

The approved initial installation and real CPU inference verification are complete. Semantic acceptance benchmarks, worker integration, and the remaining region-selection/UI work are intentionally not complete in this checkpoint.

## Environment and storage gate

- Python: 3.12.10 x64, isolated FileLens worker virtual environment.
- Separate AI venv: `D:\FileLens-AI\python\venv`.
- Installed pinned CPU packages: PyTorch 2.7.1+cpu, Transformers 4.51.3, Safetensors 0.5.3, Hugging Face Hub 0.30.2, Pillow 11.1.0.
- Model storage: `D:\FileLens-AI\models\siglip2-base-patch16-224`.
- Hugging Face cache, pip cache and temporary files are configured under `D:\FileLens-AI`.
- Pre-installation free space: D: 177.62 GB; post-installation free space: approximately 175.48 GB.
- Existing OCR venv remains separate and verified with PaddlePaddle 3.3.1/OpenCV 4.10.0.

## Candidate review

| Candidate | Runtime path | Weight/repository evidence | Fit decision |
|---|---|---|---|
| Google SigLIP2 Base Patch16 224 | PyTorch + Transformers, or a verified ONNX export with ONNX Runtime | Official Hugging Face repository is Apache-2.0; pinned repository revision must be recorded at download time. `onnx/` files are present, but their export/runtime compatibility still requires verification. | Primary candidate, pending storage approval, runtime install, actual CPU benchmark, and offline verification. |
| Smaller SigLIP/SigLIP2 variants | PyTorch/Transformers or verified ONNX export | Must be evaluated from the exact official repository and revision; no smaller model is selected by assumption. | Fallback candidate if Base cannot meet disk/RAM/latency limits. |
| OpenCLIP/CLIP-family models | PyTorch/OpenCLIP | Implementation and weight licenses must be checked separately. OpenAI CLIP's model card describes research intent and places unconstrained deployed image search out of scope without task-specific evaluation. | Not selected for production without a separate policy/license review. |

## Primary candidate facts

The official model page identifies `google/siglip2-base-patch16-224`, input size 224, patch size 16, 12 vision layers, hidden size 768, and projection size 768 in its ONNX configuration. The installed repository is pinned to revision `0ad8c6e0ff16615356a08a1ad8c8bbc8930c434e`. `model.safetensors` is 1,500,800,904 bytes and matches SHA256 `612923381c76ec5a9bed335d1c48827e3f2e506ac31b044b63b2031fadee6a0b`.

`AutoProcessor` and `AutoModel` loaded with `local_files_only=True` and `trust_remote_code=False`. The loaded class was `SiglipModel`; CPU-only feature extraction returned `[3, 768]`, and normalized vector norms were all `1.0`.

Initial synthetic scores were: identical self `1.000000`, similar solid-color image `0.998748`, and unrelated solid-color image `0.938189`. These are observations, not accuracy claims. Batch inference was `631.7 ms` for three images; a single-image run was `270.83 ms`. Peak Windows working set was approximately `0.741 GB` during model load and inference.

The implementation will use the processor's exact resize/normalization behavior, produce one normalized image vector per asset and crop, persist model name/version/revision/dimension with each vector, and invalidate vectors when any of those values change. Existing OpenCV feature vectors remain a separate provider and are not semantic results.

## Recommendation

Proceed with SigLIP2 Base as the primary production candidate using the verified pinned PyTorch/Transformers CPU path. Keep the current OpenCV provider available as a distinct exact/near-duplicate fallback, never as semantic success. Before production activation, implement the separate provider contract, model metadata/invalidation, worker IPC, offline restart test, and labeled semantic benchmark.

Sources: [official SigLIP2 model repository](https://huggingface.co/google/siglip2-base-patch16-224), [official SigLIP2 Transformers documentation](https://huggingface.co/docs/transformers/model_doc/siglip2), [official Big Vision repository/license](https://github.com/google-research/big_vision), and [OpenAI CLIP model card](https://huggingface.co/openai/clip-vit-base-patch32).
