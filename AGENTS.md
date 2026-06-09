# Agent Guide

## Context

- This project is not just software for distribution — it is a **team-project deliverable evaluated in a software architecture course**.
- It is a port of the original Qt/C++ version (TimeGrapher) to **Avalonia + C# (.NET 8)**, supporting Windows and Raspberry Pi 5 (linux-arm64) from a single codebase.
- For evaluation and interviews, a human must be able to read and trace the accumulated change history, so **every change must make its rationale visible in the history.**

## Scope

- For any code change or implementation request, always keep the change **minimal** within the required scope.
- Do not add **exception handling or fallback logic** that was not explicitly requested.
- Do not perform **refactoring for structural or performance improvement** that was not explicitly requested.
- Even if you spot what looks like an obvious bug, error, or mistake outside the requested scope, **do not fix it on your own — notify the user** and let them decide.

## Commits

- Always split commits into the **smallest logically separable units**.
- Write the commit **subject in English**, following the **Conventional Commits** spec.
  - Format: `<type>(<scope>): <description>` — scope is optional (e.g. `feat(splash):`, `fix(install.sh):`, `docs:`, `chore:`, `test:`, `ci:`, `build:`).
  - `<type>` is lowercase.
- Write the commit body in **both Korean and English**.
- For changes that affect the architecture, state in the body **which software architecture theory or tactic the change is based on**, and update the corresponding architecture view document under `docs/` when needed.

## Principles

- Base every change on **software architecture principles and the existing structure**.
- The architecture and its decisions are documented under `docs/` — check the relevant views before making changes:
  - `docs/MODULE_DECOMPOSITION_VIEW.md`, `docs/MODULE_USES_VIEW.md`, `docs/LAYERED_VIEW.md`, `docs/MVC_VIEW.md`, `docs/DATA_MODEL_VIEW.md`
  - `docs/SAP_TACTICS_ANALYSIS.md` (quality-attribute tactics), `docs/QT_CPP_TO_AVALONIA_PORTING.md` (porting rationale)
- Respect the layer dependency direction: `TimeGrapher.App` → `TimeGrapher.Core` ← `TimeGrapher.Platform.*` (Core must not depend on the UI or platform layers).

## Build & Test

```powershell
dotnet build TimeGrapherNet.sln -c Release        # build everything
dotnet test TimeGrapherNet.sln                    # run all tests (3 projects under tests/)
dotnet run --project src/TimeGrapher.App          # launch the GUI
dotnet run --project src/TimeGrapher.Verify -c Release -- --generated --byte-fixtures   # headless detection-accuracy verification
```

- After changing code, confirm the relevant tests pass before committing.
