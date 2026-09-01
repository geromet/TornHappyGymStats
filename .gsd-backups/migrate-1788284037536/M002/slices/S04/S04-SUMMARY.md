---
id: S04
parent: M002
milestone: M002
provides:
  - Deterministic per-point confidence + reason metadata on latest surfaces payload suitable for UI gradient rendering and unresolved provenance diagnostics.
requires:
  - slice: S03
    provides: Persisted `ModifierProvenance` rows linked to derived train logs with stable unresolved reason semantics.
affects:
  - S05
  - S06
key_files:
  - src/HappyGymStats.Api/SurfacesCacheWriter.cs
  - src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs
  - tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
  - .gsd/PROJECT.md
key_decisions:
  - Keep surfaces payload backward-compatible by adding confidence metadata as parallel arrays rather than mutating existing point object shape.
  - Emit deterministic fallback confidence and reason codes when provenance join rows are missing, instead of silently omitting diagnostics.
patterns_established:
  - Project provenance confidence as index-aligned additive arrays (`confidence`, `confidenceReasons`) to preserve existing consumer contracts.
  - Use deterministic status multipliers + stable reason dedupe/order so repeated imports emit reproducible confidence metadata.
observability_surfaces:
  - `/api/v1/torn/surfaces/latest` and generated `latest.json` now expose reason-code distribution directly through `gymCloud.confidenceReasons`.
  - Integration tests in `DbPipelineIntegrationTests` and `SurfaceSeriesBuilderConfidenceTests` act as contract diagnostics for confidence/reason regressions.
drill_down_paths:
  - .gsd/milestones/M002/slices/S04/tasks/T01-SUMMARY.md
  - .gsd/milestones/M002/slices/S04/tasks/T02-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-01T21:21:47.475Z
blocker_discovered: false
---

# S04: Accuracy Scoring & Surface Payload

**Shipped deterministic per-point confidence and stable provenance reason codes on `/api/v1/torn/surfaces/latest`, including explicit low-confidence fallback diagnostics when provenance joins are missing.**

## What Happened

This slice connected persisted modifier provenance to emitted surface points end-to-end. `SurfacesCacheWriter` now loads and groups `ModifierProvenance` rows by derived train log, then `SurfaceSeriesBuilder` projects deterministic confidence outputs aligned to existing gym point indices. The API/cache payload contract stayed backward-compatible by adding `gymCloud.confidence` and `gymCloud.confidenceReasons` arrays without changing existing `x/y/z/text` fields. Confidence semantics are deterministic and reproducible from provenance state (`verified`, `unresolved`, `unavailable`, unknown) and emitted reasons are deduplicated and stably ordered. Integration coverage was expanded to validate both complete and unresolved provenance scenarios plus explicit fallback behavior (`missing-provenance-record`, confidence `0.2`) when no provenance rows can be joined. Net result: downstream UI can render red→green gradients with concrete reason metadata instead of inferring confidence heuristically.

## Verification

Executed all slice-level verification commands and confirmed green results: (1) filtered surface+pipeline suite `dotnet test ... --filter "FullyQualifiedName~Surface|FullyQualifiedName~Surfaces|FullyQualifiedName~DbPipelineIntegrationTests"` passed (8/8), (2) focused DB pipeline integration suite `dotnet test ... --filter "FullyQualifiedName~DbPipelineIntegrationTests"` passed (4/4), and (3) full test suite `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` passed (38/38). Integration assertions confirm additive payload compatibility, deterministic confidence values/reason codes for verified+unresolved provenance, and fallback diagnostics for missing provenance joins.

## Requirements Advanced

- {{requirementId}} — Added API confidence/reason payload contract from persisted provenance and verified deterministic semantics.

## Requirements Validated

- {{requirementId}} — Integration tests now prove complete/unresolved/missing provenance confidence behavior in emitted latest surfaces payload.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

None.

## Known Limitations

Frontend consumption of confidence gradients/tooltips is not yet implemented (S05). Unresolved owner/faction/company acquisition and override workflow remains pending (S06), so unresolved reason prevalence may remain high until that slice lands.

## Follow-ups

S05 should lock UI mapping from confidence scalar to red→green color stops and display `confidenceReasons` in tooltip copy. S06 should prioritize reducing `missing-faction-record`, `missing-company-record`, and `missing-provenance-record` frequencies via acquisition and override flows.

## Files Created/Modified

- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — Joined provenance data into surface cache generation and emitted additive confidence/reason arrays.
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — Implemented deterministic confidence projection and stable reason aggregation/fallback behavior.
- `tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs` — Added focused unit coverage for projection determinism, reason ordering, and fallback semantics.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — Added end-to-end assertions for latest surfaces confidence payload and missing-provenance fallback.
- `.gsd/PROJECT.md` — Refreshed project state to record S04 completion and updated verification snapshot.
