# Agent Guide

## Context

- This project is not just software for distribution — it is a **team deliverable graded in a software architecture course**.
- Graders and interviewers must be able to read and trace the full change history, so **every change must record its rationale in that history.**

## Scope

- Do not add **exception handling or fallback logic** unless it was explicitly requested.
- Do not **refactor for structure or performance** unless it was explicitly requested.
- If you spot a likely bug or mistake outside the requested scope, **do not fix it yourself — tell the user** and let them decide. The exception: if the requested change cannot be completed without fixing it, fix it as part of the work and say so in the commit.
- Keep every change's impact on architectural stability and performance as small as possible.

## Commits

- Split commits into the **smallest logically separable units**.
- Do not force-push or rewrite remote history. This includes `git push --force`, `git push --force-with-lease`, and any equivalent operation that replaces already-pushed commits; if a push is rejected, report the divergence instead.
- If a normal push is rejected because the remote branch has new commits, do not force it. Run `git fetch`, inspect the ahead/behind state, rebase the local branch onto the remote branch, resolve any conflicts by preserving both the user's work and the remote changes, rerun the relevant validation, then push normally.
- Write the commit **subject in English**, following the **Conventional Commits** spec.
  - Format: `<type>(<scope>): <description>` — scope is optional (e.g. `feat(splash):`, `fix(install.sh):`, `docs:`, `chore:`, `test:`, `ci:`, `build:`).
  - `<type>` is lowercase.
- A commit message is not a place to show off expertise. Keep it clear, concise, and focused on the essentials.
- Write the commit body in **both English and Korean**, in this order (English first, then Korean):

  ```
  [en] English description of the change
  continued in English...

  [ko] 변경 내용에 대한 한글 설명
  한글 설명 계속...
  ```

- When a change affects the architecture or a design pattern, state in the body **which architectural theory or tactic it is based on**, and update the corresponding view document under `docs/for-ai/` when needed.

## Principles

Base every change on **software architecture principles and the existing structure**.

- Preserve the design patterns already used by the touched area. For UI work, respect the existing MVVM boundary: keep view state and commands in view models, keep presentation in XAML/views, keep coordination in services/controllers, and use view adapters for UI-specific operations. For platform integration, respect the Adapter boundary: translate OS or library APIs behind the existing Core/App contracts instead of leaking platform-specific types across layers.
- If a requested change appears to require breaking an established pattern, report the tradeoff before editing and prefer a pattern-preserving implementation unless the user explicitly approves the pattern change.

### Architecture & documentation

- The architecture and its decisions live under `docs/for-ai/`. Check only the relevant documents in that directory before making changes, and update the matching document whenever a change affects the system's structure:
  - `docs/for-ai/DATA_MODEL_VIEW.md`, `docs/for-ai/MODULE_USES_VIEW.md`
  - `docs/for-ai/SAP_TACTICS_ANALYSIS.md` (quality-attribute tactics)
- Never read files under `docs/architecture/` while working in this repository.
- Respect the dependency graph: `TimeGrapher.App` → `TimeGrapher.Core` / `TimeGrapher.Platform.*`, `TimeGrapher.Platform.*` → `TimeGrapher.Core`, `TimeGrapher.Verify` → `TimeGrapher.Core`. **Core must not depend on anything** (no UI or platform references).

### UI & rendering conventions

- Treat Avalonia as an intentional architecture and deployment choice, not an interchangeable UI detail. The rationale is documented in `docs/ADR/en/ADR-001.md` and `docs/ADR/ko/ADR-001.md`; UI-framework, deployment, or platform changes must stay consistent with that rationale unless the user explicitly approves changing it.
- Reuse the colors, brushes, fonts, default font sizes, and common styles defined in `src/TimeGrapher.App/App.axaml` instead of introducing new ones.
- Never hardcode graph colors. Colors flow from `App.axaml` into the graph palette through `src/TimeGrapher.App/Rendering/PlotThemePalette.cs`.
- Apply graph background, axis, and grid theming through the existing helper in `src/TimeGrapher.App/Rendering/PlotThemeHelper.cs`.
- For tab IDs, tab names, refresh intervals, or graph series definitions, follow the existing catalog structure in `src/TimeGrapher.App/Tabs/InfoTabCatalog.cs`.

## Build & Test

```powershell
dotnet build TimeGrapherNet.sln -c Release        # build everything
dotnet test TimeGrapherNet.sln -c Release         # run all tests (4 projects under tests/)
dotnet run --project src/TimeGrapher.App          # launch the GUI
dotnet run --project src/TimeGrapher.Verify -c Release -- --generated --byte-fixtures   # headless detection-accuracy verification
dotnet run --project src/TimeGrapher.Verify -c Release -- --adverse  # adverse-condition detector-quality verification
```

- After changing code, confirm the relevant tests pass before committing.
- When you add or change behavior, **add or update the tests** that cover it.
