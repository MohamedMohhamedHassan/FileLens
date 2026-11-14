# ADR-0003: AI Worker Process Architecture

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens requires OCR, image embedding generation and optional object detection. The Python ecosystem has the strongest library support for these workloads (PaddleOCR, Tesseract bindings, OpenCLIP, OpenCV). However, embedding Python dependencies into a .NET desktop application is complex. A clear process boundary keeps the .NET host stable and the AI dependencies isolated.

## Decision

Use **Python 3.12+ as a separate local process** for OCR and computer-vision workloads only. All other application logic remains in .NET.

- **IPC protocol (initial):** stdin/stdout JSON-lines. The .NET host spawns the Python worker as a child process, sends requests as JSON lines on stdin, and reads responses as JSON lines on stdout. This is the simplest protocol with no network stack, no port conflicts and no firewall issues.
- **IPC fallback:** If JSON-lines IPC proves insufficient (e.g., for streaming large binary payloads), upgrade to localhost HTTP with a per-session random token. Bind only to `127.0.0.1`.
- **Worker scope:** Narrowly limited to:
  - OCR (via `IOcrEngine` adapter)
  - Image/region embedding generation (via `IImageEmbeddingProvider` adapter)
  - Optional object/region detection
- **Phase 0 deliverable:** A health-check stub interface (`IAiWorkerHealthCheck`) in .NET that will eventually verify the Python worker is available and responsive. The actual Python worker implementation is deferred to Phase 3 (OCR) and Phase 4 (embeddings).
- **Current state:** Python is not yet installed on the development machine. This is not a Phase 0 blocker — the stub interface and its in-process mock are sufficient.

## Alternatives considered

- **In-process .NET AI libraries (ONNX Runtime, ML.NET):** Avoids a separate process, but model ecosystem and preprocessing utilities are far less mature than Python's. ONNX Runtime could be used for inference in later phases, but model preparation and evaluation still benefit from the Python ecosystem.
- **gRPC instead of JSON-lines:** Typed contracts and streaming, but adds protobuf compilation, a gRPC server in Python, and more complex error handling. Overkill for the initial narrow scope.
- **Named pipes:** More structured than stdin/stdout but platform-specific setup and harder to debug. JSON-lines over stdio is simpler and portable.
- **Embedding Python via Python.NET or IronPython:** Avoids a process boundary, but creates dependency-management complexity (conflicting native libraries, GIL contention) and makes crash isolation impossible.

## Consequences

- **Positive:** Clean process isolation — a crash in the Python worker (out-of-memory on a large model, segfault in a native library) does not bring down the .NET host.
- **Positive:** The .NET application launches and functions (metadata indexing, text search, previews) even if Python is not installed. AI features degrade gracefully.
- **Positive:** JSON-lines is debuggable with plain text tools and trivially testable with mock scripts.
- **Risk:** Process startup latency — the Python worker needs time to load models. Mitigate by starting the worker lazily on first AI request and keeping it alive for the session.
- **Risk:** stdin/stdout IPC has no built-in request multiplexing. If concurrent AI requests are needed, either queue them in .NET or upgrade to the HTTP fallback.
- **Follow-up:** Phase 3 implements the actual Python worker with OCR. Phase 4 adds embedding generation. Evaluate whether JSON-lines IPC is sufficient at that point or needs the HTTP upgrade. Document Python version and dependency installation in the project README.
