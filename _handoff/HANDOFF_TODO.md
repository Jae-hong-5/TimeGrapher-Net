# Handoff — Positions / Health redesign

**Branch:** `feat/positions-health-redesign` (based on latest `origin/main`).
**Purpose of this file:** commit-unit checklist + design spec so another agent (Codex) can continue if the current session stops mid-way.
**Delete this whole `_handoff/` folder in the final commit / before opening the PR.**

> CAUTION: this branch is on the latest `origin/main`, which moved 13 commits past the
> checkout the spec was drafted on. **Re-read each target file before editing** — line
> numbers and small details below may have shifted. Verify symbol names still exist.

Spec images in this folder: `spec-health.png`, `spec-positions.png` (rendered HTML mockups, sample data).

---

## What we're building (decisions from the design session)

Two tabs share the same per-position snapshot (`PositionSummary`). We split responsibilities:
**Positions = acquire/observe raw data**, **Health = all diagnosis (judgment)**.

### Health  =  Radar  +  unified Diagnosis rail   (see `spec-health.png`)
- Layout: content row = left **radar card** (flex, ~60%) + right **Diagnosis rail** (~470px).
- Radar: keep existing `WatchHealthRadarControl` (hexagon, 6 cardinal positions, accept-band ring, metric polygon). Move the Amplitude/Rate/Beat-error **metric toggle into the rail header**.
- Diagnosis rail (single panel), top→bottom:
  1. Header `DIAGNOSIS · unified` + metric toggle.
  2. **OVERALL** verdict: big word (OK / WATCH / ALERT) colored `VarioGood/VarioWarn/VarioBad` + chip; subline `worse of the two axes · N/10 positions · elapsed`.
  3. **LEVELS · per position vs accept band**: column headers `POS | AMP | RATE | BEAT`, then the 6 cardinal positions — `pos | amplitude | rate | beat err | status-dot`; then `Weakest: <pos> (reason)`. **Emphasize the selected-metric column** (full color/bold) and **dim the other two metric columns** (secondary text) so the Amp/Rate/Beat toggle reads clearly as selected=primary, others=context. (Review fix.)
  4. **CONSISTENCY · across positions**: 3 rows `D-SPREAD / BALANCE-WHEEL / V·H BIAS`, each `name + sublabel | reading (s/d) | chip(OK/CHECK/COLLECTING)`. The sublabel states the measure + limit (e.g. "best−worst rate gap · limit 15 s/d", "vertical-position rate spread · limit 15 s/d", "vertical − horizontal mean rate") to clarify each item's unit/meaning. (Review fix.)
  5. Criteria inline footnote: "CHECK when rate spread > 15 s/d across qualified positions. Reuses the Positions sequence — no new sensor."
- Data: radar + levels from `frame.MetricsHistory.Positions`. **Consistency from `SequenceSummary.Compute(positions)` — REUSE the existing pure computation** (currently consumed by the Positions tab). OVERALL = worse severity of {levels worst-of-band, consistency verdict}.

### Positions  =  ACTIVE hero  +  one merged table   (see `spec-positions.png`)
- **Remove** from the Positions tab: the `POSITION CONSISTENCY` block, requirement guides, unbalance banner, and the `View criteria ▾` flyout. (Consistency now lives in Health.)
- Layout: content column = top **hero row**, then **merged table** (fills).
- Hero row:
  - **ACTIVE bar** (left): watch-dial graphic + small caption `current position` (NO position name, NO "Dial up") + live readout `Rate / Amplitude / Beat err / Beats` + Collection progress bar (beats vs `VarioVerdict.MinSamples` = 30; qualified / collecting).
  - **Sequence KPIs** (right, 2×2): `X̄ rate`, `X̄ amplitude`, `Positions n/10`, `Total beats` (from `SequenceSummary`).
- **Merged table** — one row per position (`WatchPositions.All`, 10 rows). Columns:
  `POS | RATE | AMPLITUDE | BEAT ERR | BEATS | RATE RANGE vs BAND | COLLECTION`
  - **POS: always BLACK** (not red for active, not gray for unmeasured).
  - **RED IS RESERVED STRICTLY FOR "out of accept band / danger"** (reviewer's #1 fix). Only two things are red: (a) out-of-band cell values (red text), (b) the range mean-dot when its mean is outside the band. Nothing else uses red as a fill/state.
  - **Active row = neutral light-gray fill (`#eef0f1`) + a thin brand-red (`#c41230`) inset left bar** as the selection accent. **Do NOT use a red fill** for the active row — a red fill reads as out-of-band.
  - RATE/AMP/BEAT/BEATS monospace. **Values out of accept band → red** (`VarioBad`): rate vs `VarioGaugePolicy.RateAccept*`, amplitude vs `VarioGaugePolicy.AmplitudeAccept*`. Unmeasured = faint `—`.
  - **RATE RANGE vs BAND**: per-row horizontal lane — amber accept band (`VarioGaugePolicy` rate min/max), zero line, blue **min–max** bar (`PositionSummary.Rate.Min/Max`), **mean dot** (`PositionSummary.Rate.Mean`): dark navy normally, **red when mean is outside the accept band**. Scale hint `−20···0···+20 s/d` in the header.
  - **COLLECTION**: progress toward 30 beats — `30+ beats` when ≥30 (green bar), `n / 30 beats` while collecting (amber bar), `not measured` at 0. **Avoid the word "qualified"** (reads as health/normal) — use beat-count wording. Hero collection label: `52 / 30 beats` (current position beats / threshold). (Review fix.)
  - Legend above the table: `● mean / ▬ min–max range / ▮ accept band / ● mean out of band / ▬ qualified / ▬ collecting`.
- Data: **all from `PositionSummary` stats (min/max/mean/count)** — NO new Core data (TREND sparkline was dropped → lightweight). Accept bands from `VarioGaugePolicy` (shared single source).

---

## Key files (verify current paths/names before editing)
- Health: `src/TimeGrapher.App/Tabs/InfoTabRegistry.Radar.cs`, `Rendering/WatchHealthRadarModel.cs`, `Rendering/WatchHealthRadarRenderer.cs`, `Rendering/WatchHealthRadarControl.cs`
- Positions: `src/TimeGrapher.App/Tabs/InfoTabRegistry.cs` (search `WatchPositions`, `POSITION CONSISTENCY`, `View criteria`), `Rendering/MultiPositionSeqRenderer.cs`, `Rendering/SequenceSummary.cs`, `Rendering/PositionSequenceDashboardControls.cs`
- Shared: `Rendering/VarioGaugePolicy.cs` (accept bands), `Rendering/VarioVerdict.cs` (`MinSamples`, levels), `Rendering/PlotThemePalette.cs` (VarioBad/Good/Warn, AcceptBand), `Core/Shared/` (`PositionSummary`, `StatsSummary`, `BeatMetricsHistorySnapshot`)
- Tests: `tests/TimeGrapher.App.Tests/` (`WatchHealthRadarModelTests`, `VarioLogicTests`, `…`)
- Docs to update: `docs/for-ai/DATA_MODEL_VIEW.md`, `docs/for-ai/MODULE_USES_VIEW.md`, `docs/for-ai/SAP_TACTICS_ANALYSIS.md`

Conventions: read `AGENTS.md`. Commit subject in English (Conventional Commits); body in English then Korean. Smallest logically-separable commits. Reuse App.axaml colors / `PlotThemePalette`. Push to this branch (do not force-push).

Build/test:
```
dotnet build TimeGrapherNet.sln -c Release
dotnet test  TimeGrapherNet.sln -c Release
```

---

## Commit-unit checklist  (tick as you go)
- [x] **C1** docs: add `_handoff/` plan + spec (this commit)
- [x] **C2** refactor(positions): extracted the consistency verdict (OK/CHECK/COLLECTING + D-spread/balance/V·H statuses + spread readings) out of `MultiPositionSeqRenderer` into the pure `ConsistencyDiagnosis` (new) so Health can consume it. No UI/behavior change (670 tests green). + `ConsistencyDiagnosisTests`.
- [x] **C3** feat(health): `WatchHealthRadarModel.Build` now also returns `Levels` (per-position amp/rate/beat + worst-of-three status), `Consistency` (the C2 `ConsistencyDiagnosis` over `SequenceSummary.Compute(positions)`), and `Overall` (worse of band-level and consistency). `Build` gained an optional `activePosition` param (existing callers/tests unaffected). 674 tests green. Renderer still shows only the old fields — wiring is C4.
- [x] **C4** feat(health): rebuilt `InfoTabRegistry.Radar.cs` into radar (left) + unified Diagnosis rail (right, 470px): metric toggle in the header, OVERALL verdict, LEVELS list (column headers + selected-metric emphasis + status dot), Weakest, CONSISTENCY cards, criteria inline. `WatchHealthRadarRenderer` rewritten to populate the rail from the model. Verified by build + headless capture; 674 tests green.
- [x] **C5** feat(positions): removed the `POSITION CONSISTENCY` block, criteria flyout, unbalance banner and their helpers from the Positions tab; `MultiPositionSeqRenderer` is now a pure data view (table + active position) and `PositionSequenceDashboardControls` is just the two active-position labels. Obsolete consistency UI tests removed (logic covered by `ConsistencyDiagnosisTests`). 670 tests green — Positions is now data-only (table + active card).
- [x] **C6** feat(positions): merged the per-position table into 7 columns (POS/RATE/AMP/BEAT/BEATS + RATE RANGE lane + COLLECTION). New `RateRangeLaneControl` draws min–mean–max vs the accept band from `PositionSummary` stats + `VarioGaugePolicy` (no new data). POS black; rate/amplitude out-of-band values red; mean dot dark, red when out of band; active row is neutral + red left bar (App.axaml `SeqActiveRow`, no red fill). Legend added. Verified by build + headless capture; 670 tests green. ("good" swatch is theme blue `VarioGood`.)
- [x] **C7** feat(positions): replaced the side ACTIVE card with a top ACTIVE hero (watch diagram + `current position` + live rate/amplitude/beat/beats + collection bar) and sequence KPI tiles (X̄ rate, X̄ amplitude, positions n/10, total beats); the table is now full-width below. `MultiPositionSeqRenderer.UpdateHero` populates them from the active `PositionSummary` + `SequenceSummary`. Verified by build + headless capture; 670 tests green.
- [x] **C8** docs(architecture): added a Modifiability tactic row to `SAP_TACTICS_ANALYSIS.md` ("Single verdict source — another consumer over one snapshot": consistency computed once in `ConsistencyDiagnosis`, consumed by Health, Positions a pure data view, no new analysis data). `DATA_MODEL_VIEW.md` and `MODULE_USES_VIEW.md` need no change — the redesign added no Core DTO and no module-level dependency (the low-impact design point).
- [ ] **C9** test: `dotnet build` + `dotnet test` green; fix fallout; add/adjust tests for changed behavior.
- [ ] **C10** chore: **delete `_handoff/`** and open the PR.
