# FileLens project instructions for Google Antigravity

## Product goal
Build a Windows-first, local-first desktop application that indexes user-selected disks, folders and authenticated Windows/UNC network shares. It searches filenames, extracted text, OCR text, embedded images, image similarity, selected image regions and sampled video frames. It provides previews and links every match back to its original file and position.

## Non-negotiable safety and data rules
- Treat source files as read-only. Never rename, edit, move, delete, encrypt, or rewrite indexed source files.
- Never scan a drive, network share, or folder until the user explicitly adds that location. Never silently elevate privileges.
- Respect Windows ACLs and the current user's access. Do not bypass permissions or save share passwords in plaintext.
- Do not follow directory junctions/symlinks by default. Detect cycles and prevent paths escaping an approved root.
- Keep extracted text, thumbnails, previews, OCR output, embeddings and indexes in a clearly configured application-data/cache directory, separate from source files.
- Never upload indexed content, extracted text, thumbnails, embeddings or user reference images to cloud services by default. AI inference must be local unless the user deliberately enables a clearly disclosed remote provider.
- Treat all indexed files as untrusted input. Apply size limits, timeouts, parser isolation where practical, archive-bomb limits, and safe preview handling.
- Do not implement face identity recognition or assign names to people. Any future face-region similarity feature must be explicit opt-in, local-only, user-controlled, and separately reviewed for privacy/legal compliance. The default visual-search MVP uses general image/region similarity and object/scene features.
- Never claim universal content support. Unknown formats get filename/metadata indexing and a clear unsupported-content status until an adapter is implemented.

## Engineering rules
- Use a modular monolith first; do not introduce microservices, distributed infrastructure, or cloud dependencies without an Architecture Decision Record (ADR) explaining why.
- Proposed default: .NET 10 LTS WPF desktop shell and core services; Python worker only for OCR/computer-vision workloads. Verify current compatibility and licensing before choosing final packages.
- Keep contracts and domain logic independent of WPF and Python implementation details.
- Use interfaces for file discovery, text extraction, preview generation, visual asset extraction, OCR, embeddings, vector search and storage.
- All long-running operations must be cancellable, resumable where sensible, observable, bounded in resource use, and safe under partial failure.
- All UI work must be non-blocking. Use async APIs and a background job queue.
- Use dependency injection, structured logging, configuration validation, and explicit error states.
- Add tests with every feature: unit tests, adapter fixtures, integration tests, and performance tests where relevant.
- Do not weaken, delete, or rewrite tests just to make a build pass. Explain any intentional test change.
- Do not add packages before checking maintenance, license, platform support, security posture, package size and transitive dependencies.
- Do not create a package, installer, service, scheduled task, firewall rule, or startup entry without explicit approval.

## Execution workflow
1. Inspect the repository and current Git status before changing files.
2. Read `docs/TECHNICAL-IMPLEMENTATION-PLAN.md`, `docs/FORMAT-CAPABILITY-MATRIX.md`, and the relevant skill.
3. Work on one milestone/vertical slice at a time. Do not implement future phases early unless required by a documented dependency.
4. State the plan and files expected to change before broad edits.
5. Implement the smallest complete vertical slice, including UI/API/storage/tests/docs as applicable.
6. Run relevant formatting, build, unit and integration tests. Report exact commands and results; never claim tests passed unless they ran.
7. Update `docs/IMPLEMENTATION-STATUS.md` (create it if absent) with completed work, test evidence, known issues, next milestone and decisions.
8. Check `git diff` and `git status`. Do not commit unless asked.

## Definition of done
A feature is not done merely because code compiles. It must meet its acceptance criteria, handle cancellation and errors, preserve source files, respect access permissions, include tests, expose useful user feedback, and update documentation/status.

## Project conventions
- Prefer clear names and small classes/functions over clever abstractions.
- Use UTC timestamps in storage; convert to local time only in the UI.
- Store canonical full paths and normalized path keys separately; do not assume paths are case-sensitive on Windows.
- Keep original extracted text and normalized search text distinguishable.
- Search result scores must identify their meaning (exact text, OCR, perceptual hash, embedding similarity, etc.). Do not present unrelated scores as one calibrated probability.
- Every visual match must include source file ID and, where applicable, page/slide/sheet/layer/frame timestamp and source rectangle.
- Use stable IDs for files/assets. A path can change; content hashes and file identity should help reconcile safely.
