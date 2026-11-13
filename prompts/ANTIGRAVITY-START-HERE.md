# First prompt for Google Antigravity IDE

You are the lead engineer for FileLens. Before editing code, read `AGENTS.md`, `docs/TECHNICAL-IMPLEMENTATION-PLAN.md`, `docs/FORMAT-CAPABILITY-MATRIX.md`, and `docs/IMPLEMENTATION-STATUS-TEMPLATE.md` completely.

FileLens is a Windows-first, local-first desktop application for indexing only user-selected drives, folders, removable disks and authenticated SMB/UNC shares. It searches file names, extracted document text, Arabic/English OCR, embedded images, visual similarity, selected image regions, and sampled video frames. It previews results and always links them to the original source location.

## Your first task: Phase 0 only

1. Inspect only this repository and report Git status, available .NET SDKs, Python versions, Windows build tools, and existing files. Do not scan user disks, home folders, mapped drives, or network shares.
2. Read the implementation plan and format matrix. Identify uncertain requirements, platform limitations, dependency/license risks, and technical decisions that require approval.
3. Verify current official documentation for .NET 10 WPF, Python interoperability, package maintenance, and license terms before finalizing dependencies. Cite the sources in the architecture decision records.
4. Propose a modular-monolith architecture. Keep domain contracts independent of WPF and Python. Use Python only for OCR/computer-vision tasks that benefit from it.
5. Draft concise ADRs for desktop UI, document/text extraction, metadata/full-text storage, visual/vector indexing, preview cache, and Python worker boundary.
6. Create/update `docs/IMPLEMENTATION-STATUS.md` with verified environment facts, decisions, risks, tests, and the next gate.
7. Only after the design is coherent, create the smallest buildable solution skeleton: an empty WPF shell, structured logging, configuration validation, and a health-check stub for the local AI worker. Do not implement disk scanning.
8. Add tests for foundational services and configuration. Build and run tests; report exact commands and real outputs.
9. Inspect `git diff` and `git status`. Stop at the Phase 0 approval gate.

## Non-negotiable constraints

- Never use unrestricted permission modes or bypass approval prompts.
- Never add a real user drive, network share, or home directory to the IDE workspace.
- Do not scan outside the repository during setup.
- Do not modify, rename, move, delete, encrypt, or rewrite source files.
- Never start indexing until the user explicitly adds a path inside the FileLens UI.
- Respect Windows ACLs; never bypass permissions or store passwords in plaintext.
- Do not upload indexed content, extracted text, thumbnails, embeddings, or reference images to cloud AI by default.
- Do not implement face identity recognition. Initial visual search is for general image/region similarity and object/scene search.
- Do not create an installer, Windows service, scheduled task, startup entry, firewall rule, driver, or system-wide hook without explicit approval.
- Do not weaken tests or invent successful build/test results.
- Do not start Phase 1 until I explicitly approve it.

Deliver a concise Phase 0 report: environment verified, architecture proposed, files created, build/test evidence, unresolved risks, and exact approval needed for Phase 1.
