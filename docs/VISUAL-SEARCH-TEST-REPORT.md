# Phase 4 Visual Search Test Report

## Current evidence

- Release build: passed with 0 warnings and 0 errors.
- Existing and Phase 4 unit tests: 82/82 passed; the two new visual model tests cover threshold/limit validation and normalized region clamping.
- Existing integration tests: 19/19 passed.
- Python worker visual descriptor test: passed; 104-dimensional local CPU descriptor, width/height metadata, perceptual hash, JSON-lines response, and graceful shutdown.
- `pip check`: passed.
- SigLIP2 model loading: passed from the pinned local D: model directory with `local_files_only=True` and `trust_remote_code=False`.
- SigLIP2 JSON-lines worker: passed for ping, health, `embed_image`, normalized 768-dimensional output and shutdown.
- Offline flags: passed with `TRANSFORMERS_OFFLINE=1` and `HF_HUB_OFFLINE=1`; no network/model download was needed during inference.
- Release build after provider integration: passed with 0 warnings and 0 errors.

## Test coverage implemented

- Supported extensions: JPG, JPEG, PNG, TIFF, BMP, WEBP, GIF.
- Read-only ingestion and approved-root boundary: reuses existing discovery records.
- 100 MB image safety bound.
- Incremental asset identity: deterministic asset ID per indexed file plus SHA-256 content hash.
- Exact/near/visual result labels are distinct from cosine similarity scores.
- Top-K values restricted to 10, 25, 50, and 100; threshold clamped to `[0,1]`.
- Region coordinates are normalized and clamped.
- Restart-safe persistence is represented by SQLite migration 004 and idempotent upserts.

## Not yet claimable

Semantic Precision@5, Precision@10 and Recall@10 have not yet been measured. The installed SigLIP2 provider is integrated and verified at worker level, but indexed-corpus ranking, persistence/mismatch tests, and the labeled benchmark remain. Interactive drag/resize handles and clipboard paste are not enabled in this slice.

## Performance

The worker descriptor is CPU-only and local. A representative 64×48 synthetic image produced a 104-dimensional vector successfully. A larger labelled benchmark is required before setting production thresholds or claiming semantic-search quality.
