---
id: M002
title: "Modifier Provenance & Accuracy Gradient"
status: complete
completed_at: 2026-05-01T22:04:22.173Z
key_decisions:
  - Enforce taxonomy integrity with executable drift checks over source anchors to catch provenance mapping drift early (S01).
  - Model unresolved provenance as first-class persisted state with machine-readable reason codes, not implicit/transient gaps (S02/S03).
  - Keep surfaces payload backward-compatible via index-aligned additive arrays for confidence metadata (`confidence`, `confidenceReasons`) (S04).
  - Treat local manual overrides as untrusted advisory input with strict validation and explicit attribution, without mutating persisted provenance records (S06).
key_files:
  - scripts/verify-s01-taxonomy.sh
  - src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
  - src/HappyGymStats.Data/Migrations/20260501205207_AddModifierProvenanceModel.cs
  - src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
  - src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs
  - src/HappyGymStats.Api/SurfacesCacheWriter.cs
  - web/app.js
  - src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs
  - scripts/verify/s05-local-surfaces.sh
  - scripts/verify/s06-provenance-warnings.sh
lessons_learned:
  - Deterministic provenance reason codes and normalized per-scope records make downstream API/UI logic a pure projection problem instead of reconstruction-coupled inference.
  - Additive payload evolution (parallel arrays) preserved existing consumer contracts while enabling richer confidence diagnostics.
  - Local verification scripts must reflect real runtime envelope and hosting behavior (launch profile overrides, actual meta keys) to prevent false negatives in UI readiness gates.
  - File-based override enrichment is operationally useful but introduces governance debt if it becomes long-lived without ownership/audit controls.
---

# M002: Modifier Provenance & Accuracy Gradient

**Delivered an end-to-end deterministic modifier provenance pipeline that surfaces per-point confidence gradients and actionable unresolved owner/faction/company guidance across DB, API, and dashboard.**

## What Happened

M002 progressed from taxonomy discovery to operational warning workflows in six completed slices. S01 established the endpoint/log taxonomy contract and drift checks; S02 introduced the time-bounded ModifierProvenance model with constrained scope/status and unresolved reason persistence; S03 wired reconstruction to emit normalized per-scope provenance rows transactionally with deterministic unresolved placeholders; S04 projected this into additive surfaces payload confidence arrays and stable reason codes with explicit low-confidence fallback for missing joins; S05 implemented deterministic red→green confidence rendering and reason tooltips with fixture-backed frontend regression tests; S06 added deterministic provenanceWarnings projection, strict local override ingestion, and dashboard guidance with safe links, caps, and diagnostics. Cross-slice integration now provides a coherent import→reconstruct→persist→project→visualize→remediate loop with deterministic semantics and repeatable verification gates.

## Success Criteria Results

- ✅ **Validated endpoint/log taxonomy matrix with confidence-impact mapping** — S01 delivered `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md` plus `scripts/verify-s01-taxonomy.sh` drift guardrails.
- ✅ **DB schema supports time-bounded provenance + verification states** — S02 added `ModifierProvenanceEntity`, migration `20260501205207_AddModifierProvenanceModel`, DB constraints/indexes/FK, and schema-contract tests.
- ✅ **Import/reconstruction persists baseline evidence and unresolved dependencies** — S03 reconstruction emits personal verified + unresolved faction/company placeholders with stable reason codes in transactional refresh.
- ✅ **`/api/v1/torn/surfaces/latest` includes per-point confidence/reasons** — S04 added `gymCloud.confidence` and `gymCloud.confidenceReasons` additive arrays with deterministic fallback (`missing-provenance-record`, confidence `0.2`).
- ✅ **Frontend renders confidence gradient + evidence tooltips** — S05 implemented deterministic red→green marker mapping and tooltip reason rendering in `web/app.js`, validated by `tests/web/confidence-visualization.test.mjs`.
- ✅ **Actionable warnings + optional overrides workflow** — S06 added deterministic `provenanceWarnings`, diagnostics, safe profile links, and strict optional local override enrichment validated by backend/frontend tests and `scripts/verify/s06-provenance-warnings.sh`.

## Definition of Done Results

- ✅ **All slices complete** — `gsd_milestone_status` reports S01–S06 as `complete` with zero pending tasks.
- ✅ **Slice summaries exist** — Summary files confirmed for S01..S06 under `.gsd/milestones/M002/slices/S0x/S0x-SUMMARY.md`.
- ✅ **Cross-slice integration works** — S02 persistence feeds S03 reconstruction outputs; S03 provenance feeds S04 payload confidence contract; S04 contract consumed by S05 UI gradient/tooltips; S06 warning/override workflow builds on S03/S04/S05 with deterministic diagnostics.
- ℹ️ **Horizontal checklist** — No explicit horizontal checklist section present in `M002-ROADMAP.md`.

## Requirement Outcomes

- **R001:** `active → validated`.
  - Evidence: S06 verification bundle passed (`dotnet` filtered integration/confidence/override tests, web warning workflow tests, and `scripts/verify/s06-provenance-warnings.sh`) proving deterministic unresolved provenance visibility, fallback behavior, and actionable operator diagnostics.
- **R002:** `active → validated`.
  - Evidence: S04-S06 additive confidence/reason/warning contracts remained deterministic and backward-compatible across API+frontend; validated by `SurfaceSeriesBuilderConfidenceTests`, `DbPipelineIntegrationTests`, web confidence/provenance tests, and override loader tests.

## Deviations

No milestone-level plan deviations. Notable slice-level adjustment: S05 verifier script was corrected to honor `--no-launch-profile` URL behavior and real `currentVersion` meta envelope while keeping the intended readiness gate semantics.

## Follow-ups

Evaluate a future milestone for override governance (ownership, review cadence, auditability) if local manual mappings become persistent operational dependencies. Consider promoting coarse reason taxonomy into monitored operational metrics to track unresolved-category burn-down over time.
