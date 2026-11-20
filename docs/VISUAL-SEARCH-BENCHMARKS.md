# Phase 4 Visual Search Benchmarks

## Status

The production model is now installed and integrated at worker/provider level. Semantic Precision@5, Precision@10, Recall@10 and corpus-level latency values are still pending the labeled retrieval benchmark. No semantic quality values are fabricated here.

The current verified worker tests cover both providers separately: `FileLens-VisualFeature-CPU` remains a 104-dimensional low-level descriptor, while `SigLIP2VisualEmbeddingProvider` produces normalized 768-dimensional local CPU vectors. OpenCV results must not be reported as learned semantic retrieval.

Initial model-level observations (not retrieval metrics): single-image CPU inference was 270.83 ms, peak working set was approximately 0.741 GB, identical self-similarity was 1.000000, similar synthetic images scored 0.998748, and an unrelated synthetic color image scored 0.938189.

## Required benchmark protocol

After model approval, run the same preprocessing for indexing and queries over a synthetic labeled dataset containing exact duplicates, resized/compressed images, crops, similar products, logos, similar scenes, and unrelated images. Report separately for whole-image and selected-region queries:

- Precision@5, Precision@10, Recall@10.
- Median and p95 query latency.
- Median embedding time and indexing throughput.
- Peak worker memory and CPU utilization.
- Persisted vector/index size.
- Provider, model name, pinned revision, dimension, device, threshold and result limit.

Exact identity and perceptual/near-duplicate results remain separate categories from semantic similarity.
