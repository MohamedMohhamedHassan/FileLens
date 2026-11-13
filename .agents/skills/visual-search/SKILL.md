---
name: visual-search
description: Design, implement, test or debug FileLens image and region-based visual search, including embeddings, vector retrieval, ranking and source previews. Use for Google-Lens-style visual discovery features.
argument-hint: [feature-or-bug]
---

# Visual search workflow

1. Trace the current pipeline from visual-asset extraction through preprocessing, embedding, vector ID mapping, retrieval, ranking and preview.
2. Read relevant ADRs and verify the model's license, offline behavior, input constraints and redistribution rules before changing models.
3. Keep query and indexed preprocessing identical: orientation, color space, resize, normalization and crop handling.
4. Support whole-image search and user-selected crop search. Validate crop coordinates against the displayed image dimensions and preserve original coordinate mapping.
5. Separate exact/near-duplicate matches (for example perceptual hash) from semantic embedding similarity. Never describe a similarity score as a probability unless calibrated.
6. Use a two-stage retrieval strategy where appropriate: approximate nearest neighbors for candidates, then re-ranking. Measure recall and latency on a labelled test set.
7. Map every result to its source file and locator (page, slide, sheet, PSD layer or video timestamp) and optional bounding box. Preview must identify the original source.
8. Default to local inference. Do not send images, crops, embeddings or extracted source files to remote APIs.
9. Do not identify people, infer names or build identity profiles. Any face-region similarity must be a separately approved opt-in feature and receive privacy/legal review before implementation.
10. Test exact duplicates, resized/compressed copies, visually similar but different objects, selected crops, unrelated images, corrupt files and source locator accuracy.
11. Report model/version, thresholds, benchmark set, precision/recall observations, CPU/GPU timings and known limitations.
