# ADR-0004: Vector Index for Visual Search

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens visual search requires approximate nearest-neighbor (ANN) retrieval over image and region embeddings. The vector index must support add, remove, update and query operations, scale to tens of thousands of vectors on a single desktop machine, and be replaceable as better options emerge.

## Decision

Define an **`IVectorIndex` abstraction** in Phase 0 with implementation deferred to Phase 4.

- **Interface contract:** `IVectorIndex` exposes `Add`, `Remove`, `Update`, `Search(queryVector, k, threshold)` and `Count`. Vector IDs are stable identifiers that map to `VisualAsset` records in SQLite.
- **Storage mapping:** Vector IDs and their source file/asset metadata are stored in SQLite. The vector index itself is a separate binary file managed by the chosen ANN library.
- **Implementation selection (Phase 4):** Benchmark **HNSWLib.NET** (HNSW algorithm, MIT license, .NET bindings) against alternatives before committing. Evaluation criteria: query latency, memory footprint, incremental add/delete support, .NET compatibility, license and maintenance status.
- **Phase 0 deliverable:** Interface definition only. No vector library is added as a dependency yet.

## Alternatives considered

- **HNSWLib.NET (hnswlib):** Leading candidate — fast HNSW-based ANN, MIT license, .NET wrapper available. Deferred to Phase 4 for benchmarking.
- **FAISS (.NET bindings or Python worker):** Very fast, but primarily C++/Python. .NET bindings are unofficial and less maintained. Could run in the Python worker if needed.
- **Annoy (Spotify):** Simple and memory-mapped, but does not support incremental updates (requires full rebuild). Poor fit for an indexing pipeline that adds files continuously.
- **Milvus Lite / Chroma / Qdrant:** Embedded or lightweight vector databases. Add server-like dependencies and complexity beyond what a single-user desktop app needs. Could be revisited if scale requirements grow.
- **Brute-force linear scan:** Feasible for small corpora (<1k vectors) and useful as a baseline. Too slow for production-scale visual search.

## Consequences

- **Positive:** The abstraction allows Phase 1-3 to proceed without any vector dependency. Visual search is cleanly isolated.
- **Positive:** Implementation is chosen based on real benchmarks (Phase 4) rather than assumptions.
- **Risk:** If HNSWLib.NET proves unsuitable (e.g., unmaintained, missing delete support), a fallback must be identified. The abstraction makes switching low-cost.
- **Risk:** Vector index file size and memory consumption must be measured against desktop hardware constraints in Phase 4 benchmarks.
- **Follow-up:** Phase 4 benchmarks HNSWLib.NET on a representative image corpus (1k–50k vectors). Evaluate incremental delete behavior, persistence format and rebuild cost.
