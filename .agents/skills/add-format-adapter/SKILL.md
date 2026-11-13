---
name: add-format-adapter
description: Add or improve a safe FileLens parser, text extractor, visual-asset extractor or preview adapter for a file format. Use when adding support for a new extension or improving document/media extraction.
argument-hint: [extension-or-format]
---

# Add a file-format adapter

1. Inspect current adapter interfaces, capability matrix, status enum, test fixtures and dependency policy.
2. Determine whether the format is a container, text format, image, document, archive, audio or video. Detect file signatures where practical; do not trust the extension alone.
3. Research parser maintenance, supported OS/runtime, license, security history and redistribution terms before adding a dependency. Present trade-offs for any non-trivial dependency.
4. Implement the smallest adapter behind the relevant interface. Report capabilities separately: metadata, text, embedded assets, preview, OCR and visual embedding.
5. Enforce limits: input bytes, decompressed bytes, archive nesting, pages/slides/layers/frames, CPU time, memory and cancellation.
6. Treat files as hostile. Never execute macros, scripts, embedded links or external resources. Write temporary files only below the designated app temp directory and clean them up.
7. Preserve source locators: page, sheet/cell, slide, PSD layer or video timestamp. Map extracted assets back to parent file IDs.
8. Add representative fixtures: valid, malformed, encrypted/password-protected, oversized and edge-case files. Use synthetic or licensed test files, not confidential company data.
9. Update `docs/FORMAT-CAPABILITY-MATRIX.md` with supported and unsupported cases. Do not claim full support if only a preview or metadata parser works.
10. Run tests and report exact capabilities, known limitations, dependency/license and performance observations.
