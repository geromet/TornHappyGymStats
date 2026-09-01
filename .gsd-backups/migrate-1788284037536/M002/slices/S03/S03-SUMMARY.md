---
id: S03
parent: M002
milestone: M002
provides:
  - Deterministic, transactionally consistent modifier provenance persistence for personal/faction/company scopes, including unresolved dependency diagnostics.
requires:
  []
affects:
  - S04
  - S06
key_files:
  - src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs
  - src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
  - tests/HappyGymStats.Tests/HappyTimelineReconstructorBehaviorTests.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
  - tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs
  - .gsd/PROJECT.md
key_decisions:
  - Represent missing faction/company dependencies as deterministic unresolved placeholders (`unknown-faction`, `unknown-company`) with stable reason codes rather than omitting provenance rows.
  - Persist provenance in the same transactional refresh boundary as derived trains/events to avoid mixed-generation reads.
patterns_established:
  - Emit one normalized provenance record per scope per derived train so downstream confidence logic can be pure projection, not reconstruction-aware.
  - Use stable machine-readable reason codes for unresolved states to keep diagnostics and UX copy decoupled from persistence internals.
observability_surfaces:
  - Integration assertions over persisted `ModifierProvenance.VerificationStatus` and `VerificationReasonCode` by `DerivedGymTrainLogId` in DbPipelineIntegrationTests/ModifierProvenanceSchemaTests.
drill_down_paths:
  - .gsd/milestones/M002/slices/S03/tasks/T01-SUMMARY.md
  - .gsd/milestones/M002/slices/S03/tasks/T02-SUMMARY.md
  - .gsd/milestones/M002/slices/S03/tasks/T03-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-01T21:13:00.536Z
blocker_discovered: false
---

# S03: S03

**Extended reconstruction to persist per-train modifier provenance (personal verified + unresolved faction/company placeholders) atomically with derived refresh, creating deterministic confidence inputs for downstream API/frontend slices.**

## What Happened

S03 turned modifier provenance from a schema-only contract into live reconstruction output. T01 introduced Core-side provenance contracts and deterministic unresolved reason-code constants, and surfaced provenance as a first-class reconstruction run output. T02 wired ReconstructionRunner to emit three provenance records per derived train (personal/faction/company), with personal rows marked verified and missing faction/company dependencies represented as unresolved placeholders carrying stable machine-readable reason codes; the derived refresh transaction was expanded so trains, happy events, and provenance are replaced together in one generation boundary. T03 confirmed the planned persistence/diagnostic coverage already existed and validated it with targeted and full-suite runs. Net result: S04 can score confidence directly from persisted provenance state without adding reconstruction hooks, and S06 has concrete unresolved diagnostics to drive operator workflows.

## Verification

Executed slice-level verification exactly as planned: (1) targeted integration/schema coverage via `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ModifierProvenanceSchemaTests"` (pass: 5 tests), and (2) full project regression via `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` (pass: 34 tests). This proves provenance persistence cardinality, scope/status/reason-code behavior, unresolved diagnostic determinism, and no repo-wide regressions.

## Requirements Advanced

- {{requirementId}} — Baseline modifier provenance is now reconstructed and persisted per derived train with deterministic scope/status/reason semantics, enabling confidence projection inputs.

## Requirements Validated

- {{requirementId}} — Proven by passing DbPipelineIntegrationTests and ModifierProvenanceSchemaTests showing per-train persistence and unresolved dependency diagnostics.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

- {{requirementIdOr_none}} — none

## Operational Readiness

None.

## Deviations

None.

## Known Limitations

S03 does not yet expose confidence values/reason codes through API responses or frontend surfaces; it only guarantees deterministic persistence inputs and unresolved diagnostics for downstream slices.

## Follow-ups

S04 should map `ModifierProvenance` statuses/reason codes into per-point confidence values on `/api/v1/torn/surfaces/latest`. S06 should convert unresolved faction/company diagnostics into user-facing acquisition guidance and optional override inputs.

## Files Created/Modified

None.
