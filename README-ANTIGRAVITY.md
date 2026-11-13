# FileLens — Antigravity IDE Starter Kit

This kit is adapted for **Google Antigravity IDE on Windows**.

## Contents

- `AGENTS.md` — persistent project safety and engineering instructions loaded by Antigravity.
- `.agents/skills/` — six reusable skills: project bootstrap, implement phase, add format adapter, visual search, indexing diagnostics, security review.
- `docs/TECHNICAL-IMPLEMENTATION-PLAN.md` — system architecture and phased plan.
- `docs/FORMAT-CAPABILITY-MATRIX.md` — file-format coverage and limitations.
- `docs/IMPLEMENTATION-STATUS-TEMPLATE.md` — phase tracking template.
- `prompts/ANTIGRAVITY-START-HERE.md` — first prompt for Antigravity.
- `prompts/PHASE-0-ARCHITECTURE.md` — Phase 0 prompt.

## Quick start

1. Extract the folder `FileLens-Antigravity` to `D:\Projects\FileLens` (or another development location).
2. Open Antigravity IDE and choose **Open Folder** / **Open Workspace**; select the extracted `FileLens-Antigravity` folder.
3. Trust only this project folder. Do not add your whole C: or D: drive as an IDE workspace.
4. Open the Agent side panel (`Ctrl+L` on Windows/Linux, per current Antigravity documentation).
5. Keep the permission preset at **Default** or **Request Review**. Do not use unrestricted/Turbo permissions for this project.
6. Open `prompts/ANTIGRAVITY-START-HERE.md`, copy its prompt into the agent panel, and ask it to complete Phase 0 only.
7. Review the generated plan and file diffs. Approve only changes inside this repository.
8. Confirm the app builds and tests run before requesting Phase 1.

## Skills

Antigravity IDE discovers project skills under `.agents/skills/<skill-name>/SKILL.md`. The agent can activate matching skills automatically; you can also mention a skill by name in your prompt. The `implement-phase` skill expects a phase number, e.g. `implement phase 1`.

## Safety defaults

- Never add real user drives or network shares to the IDE workspace.
- FileLens itself must index only paths explicitly added by the user in the application's UI.
- Use a small synthetic test folder for development and test fixtures.
- Keep original user data read-only. Do not perform write/delete operations in indexed source roots.
- Keep extracted content, thumbnails, embeddings and indexes in an application-controlled cache/data folder.
- Do not enable cloud upload of indexed content or reference images by default.
- Do not run shell commands that scan C:\, D:\, mapped drives, or UNC paths during setup.
- Do not install services, startup tasks, drivers, or system-wide hooks during Phase 0.

## Compatibility note

The original Claude Code kit included `.claude/` skills and agent profiles. This Antigravity edition migrates the skills to `.agents/skills/` and provides `AGENTS.md`. The Claude Code-specific agent profile files are not included because Antigravity IDE's documented workspace support for custom subagents is not the same as the standalone Antigravity App/CLI surface. The reusable skills are available in the IDE.
