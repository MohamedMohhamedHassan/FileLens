# FileLens

A Windows-first, local-first desktop application for indexing and searching user-selected disks, folders, and authenticated network shares. Search by filename, extracted text, OCR text, image content, selected visual regions, and sampled video frames.

## Status

**Phase 0 — Architecture and Feasibility** (current)

The project has a buildable solution skeleton with architecture decision records, an empty WPF shell, structured logging, configuration validation, and a health-check stub. No indexing, OCR, or AI features are implemented yet.

## Prerequisites

- **Windows 11** (Windows 10 may work but is untested)
- **.NET 10 SDK** (10.0.204 or later) — [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Python 3.12+** (required from Phase 3 onward for OCR/computer-vision; not needed for Phase 0–2)

## Building

```powershell
dotnet build FileLens.slnx --configuration Release
```

Or use the build script:

```powershell
.\scripts\build.ps1
```

## Running Tests

```powershell
dotnet test FileLens.slnx --configuration Release
```

Or use the test script:

```powershell
.\scripts\test.ps1
```

## Running the Application

```powershell
dotnet run --project src\FileLens.Desktop
```

The application launches as an empty WPF shell showing the Phase 0 status and AI worker health check result.

## Project Structure

```
FileLens/
├── docs/                          # Documentation and architecture decisions
│   ├── decisions/                 # Architecture Decision Records (ADR-0001 through ADR-0008)
│   ├── TECHNICAL-IMPLEMENTATION-PLAN.md
│   ├── FORMAT-CAPABILITY-MATRIX.md
│   └── IMPLEMENTATION-STATUS.md
├── src/
│   ├── FileLens.Domain/           # Interfaces, value objects, domain models
│   ├── FileLens.Application/      # Configuration, services, use cases
│   ├── FileLens.Infrastructure/   # SQLite, filesystem, adapters (future)
│   └── FileLens.Desktop/          # WPF application shell
├── tests/
│   ├── FileLens.UnitTests/        # Unit tests for domain and application logic
│   └── FileLens.IntegrationTests/ # Integration and smoke tests
├── scripts/                       # Build and test scripts
└── FileLens.slnx                  # .NET solution file
```

## Architecture

FileLens uses a modular monolith architecture with clean separation:

- **Domain** — Core interfaces and models, no external dependencies
- **Application** — Configuration, services, orchestration logic
- **Infrastructure** — Implementations (SQLite, filesystem, adapters)
- **Desktop** — WPF UI shell, DI composition root

See [Architecture Decision Records](docs/decisions/) for detailed design rationale.

## Privacy and Safety

- **Local-first**: All processing happens on your machine. No cloud uploads by default.
- **Read-only**: Source files are never modified, moved, or deleted.
- **User-controlled**: Only user-selected locations are indexed.
- **Permission-respecting**: Windows ACLs are honored; access is never elevated silently.

## License

TBD

