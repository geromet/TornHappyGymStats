---
id: S06
parent: M002
milestone: M002
provides:
  - Actionable unresolved owner/faction/company provenance workflow in API + dashboard, including optional validated local override enrichment and explicit diagnostics.
requires:
  - slice: S03
    provides: Persisted unresolved provenance rows and reason semantics for missing owner/faction/company context.
  - slice: S04
    provides: Confidence and reason-code projection contract in surfaces payload.
  - slice: S05
    provides: Deterministic frontend confidence rendering pipeline and local surfaces verification baseline.
affects:
  - S07
key_files:
  - src/HappyGymStats.Api/SurfacesCacheWriter.cs
  - src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
  - tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs
  - web/app.js
  - tests/web/provenance-warnings-workflow.test.mjs
  - scripts/verify/s06-provenance-warnings.sh
  - web/data/surfaces/modifier-overrides.sample.json
key_decisions:
  - Keep warning payload additive/backward-compatible and deterministic in ordering/grouping.
  - Treat override source as untrusted input with strict validation and bounded acceptance.
  - Keep manual override enrichment transparent via explicit metadata flags rather than implicit mutation.
patterns_established:
  - Deterministic unresolved-warning projection with bounded per-log fanout and reason-preserving grouping.
  - Partial-accept parser pattern for local operator config: reject invalid entries, preserve valid subset, emit skip diagnostics.
  - UI safety pattern for operator diagnostics: safe link construction, explicit fallback markers, deterministic cap + overflow messaging.
observability_surfaces:
  - `series.gymCloud.provenanceWarnings` payload for operator-facing unresolved guidance.
  - `meta.provenanceWarningsDiagnostics` for warning totals, malformed-row skips, and override load/skip/read/parse outcomes.
  - `scripts/verify/s06-provenance-warnings.sh` for repeatable end-to-end local verification of warning workflow artifacts.
drill_down_paths:
  - .gsd/milestones/M002/slices/S06/tasks/T01-SUMMARY.md
  - .gsd/milestones/M002/slices/S06/tasks/T02-SUMMARY.md
  - .gsd/milestones/M002/slices/S06/tasks/T03-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-01T22:00:24.902Z
blocker_discovered: false
---

# S06: Owner/Faction/Company Data Acquisition Workflow

**Shipped deterministic provenance warning workflows across API + dashboard, with actionable profile guidance and optional validated local faction/company overrides that enrich warnings without mutating provenance storage.**

## What Happened

S06 closed the operator acquisition loop for unresolved modifier provenance by projecting unresolved rows into additive `provenanceWarnings` records in the surfaces payload, preserving existing confidence semantics and fallback contracts. The API path now groups unresolved rows deterministically by log/scope/reason/link target, bounds warning fanout for surge safety, and emits diagnostics that distinguish truly empty warnings from query failures or malformed-row skips. In parallel, Core introduced `ModifierOverrideLoader` to ingest optional local/manual faction/company mappings with strict schema validation, entry/field bounds, deterministic duplicate handling, and explicit parse/read/skip diagnostics; these overrides enrich operator guidance only and do not alter persisted provenance rows. The dashboard now renders a dedicated warnings section with actionable copy, safe profile-link construction, manual-override attribution, malformed-payload fallback markers (`missing-provenance-record`), and deterministic capped display with overflow messaging. Together these changes make unresolved owner/faction/company gaps visible and actionable directly in UI while keeping deterministic red→green confidence behavior unchanged.

## Verification

Ran all slice-level verification gates from the plan and confirmed pass: (1) `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests|FullyQualifiedName~ModifierOverride"` (12 passed, 0 failed), validating warning projection determinism, malformed-row handling, confidence-contract stability, and override loader behavior; (2) `node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs` (12 passed, 0 failed), validating warning workflow rendering/fallback/capping/manual-override messaging while preserving confidence visuals; (3) `bash scripts/verify/s06-provenance-warnings.sh`, validating runtime artifact generation and warnings payload shape checks with explicit empty-state acceptance.

## Requirements Advanced

- R001 — surfaced unresolved provenance as explicit actionable warning contracts across API and dashboard while preserving deterministic fallback semantics.
- R002 — added optional, bounded local faction/company override enrichment path with transparent manual-source attribution and strict validation diagnostics.

## Requirements Validated

- R001 — validated by passing DbPipelineIntegrationTests + web provenance warning workflow tests + s06 verify script proving deterministic warning visibility and fallback behavior.
- R002 — validated by passing ModifierOverride loader tests and integration coverage showing strict validation, graceful fallback, and non-destructive provenance semantics.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

None beyond expected implementation detail choices; task-level plan intent was preserved.

## Known Limitations

Manual overrides are local/operator-supplied guidance only and currently rely on file-based lifecycle management; no centralized governance/audit workflow is included in this slice.

## Follow-ups

Consider a future milestone slice for override governance (ownership, review cadence, audit trail) if local/manual mappings become long-lived operational dependencies.

## Files Created/Modified

- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — Added deterministic provenance warning projection, diagnostics emission, and override enrichment hooks.
- `src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs` — Implemented optional local override loader with strict validation, bounds, duplicate handling, and diagnostics.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — Extended integration assertions for warning cardinality/order/fallback and malformed-row handling.
- `tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs` — Added parser validation, malformed input fallback, and duplicate handling tests.
- `web/app.js` — Added actionable provenance warning rendering, safe links, fallback markers, and capped display logic.
- `tests/web/provenance-warnings-workflow.test.mjs` — Added frontend contract tests for warning workflow behavior and safety/fallback semantics.
- `scripts/verify/s06-provenance-warnings.sh` — Added slice-level verification script for end-to-end warning payload checks.
- `web/data/surfaces/modifier-overrides.sample.json` — Added sample local/manual override file for operator guidance.
