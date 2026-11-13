---
name: security-review
description: Review FileLens code, design or changes for file-system boundary, parser, privacy, credential, local API, cache, archive and supply-chain risks. Use before releases and when adding any input parser or network-share feature.
---

# FileLens security review

Review the changed code and its call paths for:

- Scope: only user-approved roots are enumerated; no implicit whole-disk scan or privilege escalation.
- Authorization: access follows the current Windows account and SMB ACLs; no permission bypass.
- Path safety: traversal, junction/symlink escapes, UNC parsing, alternate data streams and temp-file collisions.
- Parser safety: hostile/corrupt files, macros/scripts, archive bombs, decompression limits, parser crashes, timeouts and resource exhaustion.
- Data protection: source files remain read-only; cache/index ACLs; credential storage; no sensitive content in logs or telemetry.
- Network exposure: local services bind only to loopback, use authentication, and reject untrusted origins/calls.
- Model privacy: no external image/text upload by default; explicit consent and clear notice for any remote feature.
- Supply chain: package/model licenses, pinned versions, hashes/signatures and update strategy.
- Face-related functions: no identity labels/profiles; optional local face-region similarity requires explicit opt-in, retention controls and separate review.

For each finding provide severity, evidence (file and line), attack/failure scenario, impact and a concrete remediation. Do not edit code during a read-only review unless the user explicitly asks for fixes. Re-run relevant tests after approved fixes.
