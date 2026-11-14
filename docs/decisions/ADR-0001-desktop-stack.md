# ADR-0001: Desktop Application Stack

- Status: Accepted
- Date: 2026-11-13
- Decision makers: Phase 0 bootstrap

## Context

FileLens is a Windows-first, local-first desktop application for file indexing and search. It needs deep Windows filesystem integration (NTFS, UNC shares, ACLs, long paths), a mature UI framework for data-dense search results and previews, and a runtime with long-term support.

## Decision

Use **.NET 10 LTS + WPF + MVVM** as the desktop application stack.

- **Runtime:** .NET 10 LTS (supported through November 2028). Verified: SDK 10.0.204 is installed on the development machine.
- **UI framework:** WPF with MVVM pattern. WPF provides native Windows rendering, data binding, rich control templates and accessibility support.
- **Domain/application logic:** C# class libraries (`FileLens.Domain`, `FileLens.Application`, `FileLens.Infrastructure`, etc.) with no WPF dependencies. All domain logic is testable independently of the UI.
- **Dependency injection:** `Microsoft.Extensions.DependencyInjection` — standard, well-documented, compatible with the full Microsoft.Extensions ecosystem.
- **Structured logging:** `Microsoft.Extensions.Logging` as the abstraction, with `Serilog` as the concrete provider. Serilog offers structured event data, file sinks, and enrichers without requiring a logging server.
- **Configuration:** `Microsoft.Extensions.Configuration` with JSON file and environment variable providers. Validated at startup.

All contracts and interfaces are defined in framework-agnostic class libraries so that the WPF shell can be replaced (e.g., with WinUI 3 or a console host for testing) without rewriting business logic.

## Alternatives considered

- **WinUI 3 / Windows App SDK:** More modern XAML controls and rounded-corner aesthetics, but smaller ecosystem, less mature tooling, fewer third-party controls, and packaging constraints (MSIX-only historically). Higher risk for a file-heavy desktop app that needs admin-free installs and broad Windows 10/11 support.
- **Electron / Tauri + web frontend:** Cross-platform potential, but FileLens is Windows-first. Electron adds significant memory overhead and complicates deep filesystem/ACL integration. Tauri is younger and would require bridging to .NET or rewriting in Rust.
- **Avalonia UI (.NET cross-platform):** Promising for cross-platform, but FileLens targets Windows only in the MVP. Avalonia's Windows-specific filesystem integration is less proven than WPF's. Could be revisited if cross-platform becomes a requirement.
- **.NET MAUI:** Primarily targets mobile and has limited desktop maturity on Windows. WPF remains the recommended choice for Windows-only desktop apps with complex layouts.

## Consequences

- **Positive:** Mature framework with extensive documentation, large control library, strong data-binding and template support, direct access to Windows APIs (P/Invoke, COM), and a long support window.
- **Positive:** Domain logic in plain C# libraries enables unit testing without a UI host and allows future UI framework migration.
- **Risk:** WPF is Windows-only; if cross-platform becomes a requirement, the UI layer must be rewritten (but domain logic is preserved by design).
- **Risk:** .NET 10 LTS end-of-support in November 2028 means a runtime upgrade will be needed within ~2 years. Plan for this in Phase 7-8.
- **Follow-up:** Confirm .NET 10 Desktop Runtime availability on all target Windows versions before first external release.
