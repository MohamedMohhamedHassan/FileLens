---
name: project-bootstrap
description: Inspect or initialize the FileLens repository, validate the development environment, create architecture decisions and a buildable skeleton. Use at project start or when restructuring the solution.
---

# Project bootstrap

1. Read root `CLAUDE.md`, the technical plan, capability matrix and current implementation status.
2. Inspect `pwd`, Git status, repository files and installed SDK/tool versions. Never inspect user drives or shares as part of bootstrap.
3. Use Plan Mode before broad changes. Report assumptions, missing prerequisites and intended file changes.
4. Verify current package versions, platform support, maintenance and licenses from authoritative sources. Do not assume a package/model is redistributable.
5. Create ADRs before locking architecture choices. Keep the app modular and local-first.
6. Create only the agreed skeleton for the current phase. No source-file scanning, OCR, embedding or media extraction during Phase 0.
7. Add build/test scripts and update README plus `docs/IMPLEMENTATION-STATUS.md`.
8. Run the exact build and tests. Distinguish executed checks from proposed checks.
9. Review `git diff` and stop at the phase gate. Do not continue to the next phase without a new user request.
