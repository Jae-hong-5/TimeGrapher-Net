---
name: check-adr
description: Review this project's Architecture Decision Records (docs/ADR/en, docs/ADR/ko) against the team's ADR template and writing guidance. Checks Decision-vs-Rationale separation, explicit rejected alternatives, balanced consequences, valid Status values, template section order, and en↔ko parity, then reports findings (file · severity · issue · fix). Use when asked to review, check, audit, or improve ADRs, or when the user types /check-adr.
---

# check-adr — ADR review for TimeGrapher-Net

Review the project's ADRs against the team's standard and report concrete, minimal fixes. ADRs live in `docs/ADR/en/` and `docs/ADR/ko/` (one English + one Korean file per decision, e.g. `ADR-002.md`). The authoritative template is the pmerson template at `github.com/lgcmu2026-team5/ADR-template`.

## When to use

The user asks to review / check / audit / improve / "정리" an ADR or the ADR set, or types `/check-adr`. Optional arg = a specific ADR number (e.g. `002`); with no arg, review every ADR pair.

## The standard

### Template structure & order (pmerson)

Every ADR follows this exact order. **No `## Context` heading** — the forces go directly under the title.

```
# ADR N: brief decision title
<forces: technological, cost-related, project-local constraints/requirements>
## Decision        active voice, full sentences — "We will..." / "우리는 ~할 것이다"
## Rationale       why; MUST include rationale for significant *rejected* alternatives
## Status          [Proposed | Accepted | Deprecated | Superseded]
## Consequences    resulting context; MUST list negatives/trade-offs, not just positives
```

House conventions in this repo:
- Korean files use the **same English section headings** (`## Decision`, `## Rationale`, `## Status`, `## Consequences`) — body text is Korean.
- Consequence sub-labels: `Positive:` / `Negative / trade-offs:` (en) and `긍정적:` / `부정적 / 트레이드오프:` (ko), each followed by a blank line before its bullets.
- Status value when accepted: `Accepted` (en) / `승인됨(Accepted)` (ko).
- Title format is uniform: `# ADR N: ...`.

### Decision vs Rationale (the core distinction)

| Record | Answers |
|---|---|
| **Decision** | What will we do? |
| **Rationale** | Why this, and why NOT the serious alternatives? |
| **Consequences** | What becomes true / easier / harder / riskier / costlier after the decision? |

Rationale turns meeting-local reasoning into shared team knowledge: it socializes the decision, acts as external memory, surfaces weak assumptions, and prevents the same debate a year later. **Why is forgotten fast — capture evidence, assumptions, constraints, and rejected alternatives.**

## Review checklist

For each ADR (and each en/ko file), confirm:

1. **Section order & headings** match the template (forces under the title, no `## Context`; order Decision → Rationale → Status → Consequences).
2. **Decision** is active voice and separated from Rationale (what, not why).
3. **Rejected alternatives** are explicitly described — so a future reader won't repeat the dead end.
4. **Context = forces** (constraints/requirements/project-local conditions), not just a description of the structure.
5. **Status** is one of the four valid values (and links to the superseding ADR if Superseded).
6. **Consequences** are balanced — negatives and trade-offs are not hidden.
7. **en ↔ ko parity**: same section set, order, decisions, and rejected alternatives; no stray markup (leftover `---` rules, bilingual inline duplication); uniform title format and labels.

### Is the decision even ADR-worthy?

Worth an ADR when it: affects requirements (functional or quality), required a technical experiment/prototype/spike, involved significant discussion, or had a strong rejected alternative. Local implementation choices are usually too small.

## Procedure

1. **Locate** the ADRs to review (the arg's number, or all pairs under `docs/ADR/en` + `docs/ADR/ko`). Read both language files for each.
2. **Check** each file against the checklist above; note deviations as `file · severity (high/med/low) · issue · minimal fix`.
3. **Report** findings grouped by ADR, **written in Korean (한글)** — the conversational review report, finding descriptions, and fix suggestions all in Korean. (Per the user's standing preference; code, file content edits, and commit subjects still follow `AGENTS.md`.) If clean, say so. Recommend fixes; apply them only if the user asked to improve/fix (not just review).
4. **(Thorough mode)** For a deep audit, adversarially verify findings before reporting — see below.
5. If you applied fixes and the user asks to commit, follow `AGENTS.md`: English Conventional-Commits subject (`docs(adr): ...`), bilingual `[en]`/`[ko]` body, smallest logical units. Branch first unless the user explicitly says commit to `main`.

## Thorough mode (adversarial verification)

For a comprehensive audit, run a review→verify workflow so each finding is independently confirmed against the file before it is reported (kills plausible-but-wrong nits):

- **Review** (one agent per ADR pair + one cross-file consistency agent): read the files, return structured findings `{file, severity, issue, suggestion}` against the template + checklist + parity. Embed the standard above in each prompt; agents Read the real files as ground truth.
- **Verify** (one agent per finding): re-read the file and judge skeptically — `real` (genuine deviation?) and `inScope` (not a deliberate house-style choice or out-of-scope refactor?). Default `real=false` when it can't be confirmed against the file text.
- Keep only findings where `real && inScope`.

This mirrors the workflow used in the review that produced this skill: 5 ADR files normalized to the template, with only 2 low-severity parity nits surviving adversarial verification.

## Scope notes

- Per `AGENTS.md`: don't fix bugs outside the requested scope — report them and let the user decide. Don't refactor doc structure beyond template/parity conformance.
- ADRs are graded course artifacts: every change must be traceable in git history with recorded rationale.
