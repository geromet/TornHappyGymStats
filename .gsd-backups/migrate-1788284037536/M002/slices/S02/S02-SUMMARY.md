---
id: S02
parent: M002
milestone: M002
provides:
  - Time-bounded provenance persistence model (personal/faction/company) with verification lifecycle state and deterministic migration/test contract for downstream reconstruction.
requires:
  []
affects:
  - S03
  - S04
  - S06
key_files:
  - src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
  - src/HappyGymStats.Data/HappyGymStatsDbContext.cs
  - src/HappyGymStats.Data/Migrations/20260501205207_AddModifierProvenanceModel.cs
  - src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
  - tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs
key_decisions:
  - Use explicit scope/status check constraints and required identifier fields at the DB layer to fail fast on malformed provenance rows.
  - Model unresolved verification as first-class persisted state with machine-readable reasons so downstream reconstruction/API can explain confidence gaps deterministically.
patterns_established:
  - Pair EF check constraints with schema-contract tests that assert both positive round-trip behavior and negative invalid-value rejection.
  - Treat unresolved verification as a persisted diagnostic surface rather than an inferred transient state.
observability_surfaces:
  - Queryable unresolved provenance rows via `VerificationStatus` + `UnresolvedReasonCode`/reason fields in `ModifierProvenance`.
  - Regression tripwires in DbContext/provenance tests that fail immediately on schema or constraint drift.
drill_down_paths:
  - .gsd/milestones/M002/slices/S02/tasks/T01-SUMMARY.md
  - .gsd/milestones/M002/slices/S02/tasks/T02-SUMMARY.md
  - .gsd/milestones/M002/slices/S02/tasks/T03-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-01T20:56:43.533Z
blocker_discovered: false
---

# S02: S02

**Shipped a DB-backed modifier provenance model with constrained personal/faction/company evidence scopes, time-bounded intervals, verification states, reversible migration, and schema-contract tests for unresolved-state diagnosability.**

## What Happened

S02 closed the data-layer contract for modifier provenance ahead of reconstruction work. T01 introduced `ModifierProvenanceEntity` as the canonical persistence shape for personal/faction/company evidence intervals, including `ValidFromUtc`/`ValidToUtc`, verification lifecycle state (`verified`, `unresolved`, `unavailable`), and machine-readable reason fields. DbContext wiring added required-field enforcement, scope/status check constraints, indexes for scope+interval lookups and status filtering, and FK linkage to `DerivedGymTrainEntity.LogId` for downstream reconstruction joins. T02 materialized the model as migration `20260501205207_AddModifierProvenanceModel` with scoped, reversible Up/Down operations and aligned model snapshot state. T03 expanded contract tests to pin provenance schema/index presence, validate scope/status constraint behavior, verify open-ended and bounded interval round-trips with UTC normalization, and prove unresolved verification rows remain queryable for diagnostics. Net result: persistence and migration contracts are closed and test-guarded; runtime reconstruction consumption is intentionally deferred to S03.

## Verification

Executed slice-level verification commands from the plan and confirmed all pass: (1) `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests|FullyQualifiedName~ModifierProvenance"` passed (7/7), validating provenance schema and diagnostics contract coverage; (2) `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` passed (33/33), confirming no regression across the full test suite. Observability/diagnostic surface for this slice is verified through queryable unresolved provenance state plus regression tests that fail on drift.

## Requirements Advanced

- {{requirementId}} — Added persistence contract and tests needed to represent and query modifier provenance completeness over time.

## Requirements Validated

- {{requirementId}} — Contract validated by passing DbContext/provenance schema tests and full test-suite regression pass.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

- {{requirementIdOr_none}} — none

## Operational Readiness

None.

## Deviations

None.

## Known Limitations

Runtime reconstruction wiring and unresolved dependency propagation are intentionally deferred to S03; this slice only closes persistence/migration/test contracts.

## Follow-ups

In S03, consume `ModifierProvenance` in reconstruction joins keyed by derived train log identity and emit unresolved faction/company dependency states using the persisted verification status/reason fields.

## Files Created/Modified

- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs` — New provenance entity with scope, interval, verification state, and unresolved reason fields.
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs` — DbSet registration plus constraints/index/FK wiring for provenance model.
- `src/HappyGymStats.Data/Migrations/20260501205207_AddModifierProvenanceModel.cs` — Migration adding provenance table, constraints, indexes, and FK with reversible Down path.
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs` — Snapshot update aligning EF model with added provenance entity.
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` — Extended schema and boundary/negative tests for provenance persistence contract.
- `tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs` — Dedicated provenance round-trip, UTC interval, unresolved diagnostics, and invalid domain tests.
