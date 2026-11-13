---
name: indexing-diagnostics
description: Diagnose FileLens indexing problems such as missing files, stale results, slow scans, UNC share failures, denied access, watcher overflow, corrupt files or stuck jobs.
argument-hint: [symptom]
---

# Indexing diagnostics

1. Ask for or inspect the affected root, file type, current job state, sanitized logs and last successful scan. Do not scan additional paths.
2. Check path validation, access under the current user, offline/network state, exclusions, file type detection and per-capability status.
3. Check queue backpressure, cancellation tokens, retries, parser timeouts, resource limits, database transactions and vector-index synchronization.
4. Treat FileSystemWatcher as a hint only. If its buffer overflowed or a UNC share is unreliable, propose a bounded reconciliation scan of the already-approved root.
5. Check long paths, locked/open files, temporary files, junctions, symlinks and path normalization/case behavior.
6. Reproduce with a minimal synthetic fixture and add a regression test before changing implementation.
7. Never disable ACL checks, turn off security limits globally, or request administrator privileges as a workaround.
8. Never log document text, credentials or private file content. Redact user paths in shared diagnostics unless the user approves disclosure.
9. State root cause only when evidenced. Otherwise list hypotheses and the next safe diagnostic step.
