---
name: implement-phase
description: Implement the next approved FileLens milestone as a tested vertical slice. Use when the user asks to start or continue a numbered phase from the technical implementation plan.
---

# Implement an approved phase

1. Read `docs/TECHNICAL-IMPLEMENTATION-PLAN.md`, `docs/IMPLEMENTATION-STATUS.md`, relevant ADRs and the skill relevant to the feature.
2. Confirm the requested phase is approved and identify its acceptance criteria. If the requested phase depends on unfinished work, report the blocker instead of skipping dependencies.
3. Inspect Git status and existing code. Never overwrite uncommitted user work.
4. Write a small implementation plan listing files/modules, data changes, tests, security implications and rollback risks.
5. Implement one vertical slice at a time. Preserve abstractions and avoid speculative frameworks.
6. Add tests for success, malformed inputs, access denied, cancellation, timeouts, large files and partial failures as appropriate.
7. Run formatter, build, unit tests and the narrowest useful integration tests. Run performance tests when the phase changes throughput or memory usage.
8. Inspect results, fix regressions, and do not suppress tests to get a green build.
9. Update status, ADRs, capability matrix and README where needed.
10. Review `git diff`; summarize completed acceptance criteria, test commands/results, limitations and next phase. Stop at the requested phase boundary.
