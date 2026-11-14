# ADR-0008: Privacy Controls and Logging Strategy

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens indexes potentially sensitive files: personal documents, business data, legal records, medical images. Logging and diagnostics must not inadvertently expose file paths, filenames, document text or image content. The application must not send any data to external services without explicit user consent.

## Decision

Use **structured logging via Serilog** with strict default privacy controls and opt-in diagnostics.

### Logging

- **Framework:** `Microsoft.Extensions.Logging` as the abstraction; `Serilog` as the provider.
- **Sinks:** File sink to `%LOCALAPPDATA%\FileLens\logs\`, rolling daily, configurable retention (default: 7 days).
- **Default log content policy (Privacy Mode):**
  - ✅ Log: operation names, timing, counts, error categories, component names, version info, configuration keys (not values containing paths).
  - ❌ Do NOT log: file paths, filenames, directory names, document text, OCR output, search queries, thumbnail content, embedding vectors, user-identifiable information.
  - File references in logs use opaque IDs (e.g., `FileId=3f8a...`) that can be correlated only by someone with access to the local database.
- **Diagnostic Mode (opt-in):**
  - User explicitly enables "Diagnostic logging" in Settings.
  - Paths and filenames are included in log entries for troubleshooting.
  - Document text and search queries remain excluded even in diagnostic mode.
  - A clear warning is shown when enabling: "Diagnostic logs may contain file paths from your indexed locations."
  - Before exporting logs (e.g., for a bug report), the user reviews and confirms what will be shared.

### Telemetry

- **No telemetry.** The application does not phone home, send usage analytics, crash reports or any other data to external services.
- **No cloud uploads by default.** All AI inference (OCR, embeddings, detection) runs locally via the Python worker.
- **Remote AI providers:** If a future phase adds optional cloud-based AI (e.g., a hosted embedding API), it must be:
  - Disabled by default
  - Enabled only by explicit user action in Settings
  - Accompanied by clear disclosure of what data is sent and to whom
  - Controllable per-feature (e.g., "Use cloud OCR" separate from "Use cloud embeddings")

### Configuration Validation

- All configuration is validated at startup. Invalid or missing required settings produce clear error messages in the UI, not silent failures.
- Logging configuration (level, sink, retention) is part of validated startup.
- Privacy-sensitive settings (diagnostic mode, any future remote provider) default to the most private option.

## Alternatives considered

- **NLog instead of Serilog:** Comparable feature set, but Serilog's structured logging and enricher ecosystem are a better fit for the property-based redaction approach needed here.
- **Windows Event Log:** Useful for service applications, but desktop apps typically use file logs. Event Log entries are visible to other local users and administrators, which conflicts with privacy goals.
- **Optional telemetry (opt-in):** Even opt-in telemetry requires infrastructure (collection server, privacy policy, data retention rules). Not justified for a local-first desktop app with no cloud backend.
- **Log path redaction via regex post-processing:** Fragile and error-prone. Structured logging with explicit property control is more reliable — paths are simply not included in log templates by default.

## Consequences

- **Positive:** Users can trust that the application does not leak their file paths or document content through logs or network calls.
- **Positive:** Diagnostic mode provides a path for troubleshooting without permanently degrading privacy.
- **Positive:** Structured logging with Serilog makes it straightforward to add context properties that are safe (counts, durations, IDs) while excluding sensitive ones.
- **Risk:** Opaque IDs in logs make debugging harder without database access. Diagnostic mode is the escape hatch, but developers must remember to ask users to enable it when investigating issues.
- **Risk:** "No telemetry" means no automatic crash reporting. Bugs in the field are discovered only when users report them manually. Mitigate with clear in-app error reporting UI and log export.
- **Follow-up:** Implement a log-export wizard that lets the user review, redact and approve log content before sharing. Define the Serilog enricher and destructuring policy in the application bootstrap code.
