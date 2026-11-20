# Phase 4 Visual Search Architecture

## Scope

This Phase 4B slice adds a production local SigLIP2 provider beside the existing OpenCV descriptor. OCR and FTS5 remain unchanged.

```text
Approved root
    |
SafeFileDiscoveryService -> IndexedFile metadata
    |
VisualIndexingService -- read-only bytes --> IImageEmbeddingProvider (SigLIP2)
    |                                           |
    |                                    dedicated visual Python worker
    |                                    embed_image / embed_batch commands
    |                                           |
    +--> VisualAssets + VisualEmbeddings (SQLite, migration 004)
                                                |
Reference image -> provider -> IVectorIndex -> cosine-ranked results
                                                |
                                  file ID/path + match type + score
```

## Contracts

- `IImageEmbeddingProvider`: local provider boundary with provider, model, pinned revision, preprocessing and dimension metadata.
- `SigLIP2VisualEmbeddingProvider`: uses the D: Torch venv, `AutoProcessor`, `AutoModel`, local-only loading, CPU inference and normalized 768-dimensional vectors.
- `LocalVisualEmbeddingProvider`: remains available as the separate OpenCV `FeatureSimilarity` provider.
- `VisualWorkerClient`: separate process lifecycle from `OcrWorkerClient`; sets offline Hugging Face flags and never launches the PaddleOCR venv.
- `IVectorIndex`: upsert, remove, and thresholded top-K retrieval.
- `VisualIndexingService`: supported image extensions, 100 MB safety bound, cancellation, read-only ingestion, SHA-256 content identity, and deterministic asset IDs.
- `RegionSelection`: normalized X/Y/Width/Height values clamped to `[0,1]`; current WPF slice exposes the model and whole-image search. Drag handles and clipboard paste remain UI work for the next approved iteration.

## Persistence

Migration 004 creates `VisualAssets`, `VisualEmbeddings`, `VisualIndexJobs`, and `VisualIndexErrors`. Migration 005 adds provider, model revision and preprocessing metadata. SQLite is the source of truth. Embeddings are float32 BLOBs; retrieval filters by provider/model before linear cosine ranking, preventing mixed vectors.

## Safety

Only files already discovered under approved roots are indexed. Source bytes are read-only. Unknown formats are ignored by the visual indexer. Offline flags prevent model downloads during inference. No face identification, person re-identification, or remote inference is implemented. Interactive region handles and clipboard paste remain outside this checkpoint.
