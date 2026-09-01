# S02: S02 — UAT

**Milestone:** M002
**Written:** 2026-05-01T20:56:43.533Z

# S02: S02 — UAT

**Milestone:** M002
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice ships persistence schema + migration + contract tests (no user-facing runtime flow yet), so proving migration artifacts and deterministic test evidence is the correct acceptance method.

## Preconditions

- Repository is at S02-complete state with migration `20260501205207_AddModifierProvenanceModel` present.
- .NET 8 SDK is installed.
- Test project `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` builds successfully.

## Smoke Test

Run:

1. `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierProvenance"`
2. Confirm all filtered provenance tests pass and report zero failures.

## Test Cases

### 1. Provenance schema contract is present and constrained

1. Run `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"`.
2. Inspect test output for checks covering `ModifierProvenance` table existence and expected index/column contract.
3. **Expected:** Tests pass, proving schema includes provenance fields and indexes and rejects malformed status/identifier inserts per constraints.

### 2. Scope/status + interval behavior round-trips correctly

1. Run `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierProvenanceSchemaTests"`.
2. Confirm tests execute round-trip inserts for personal/faction/company scopes with verification status and reason fields.
3. Confirm interval tests include both open-ended (`ValidToUtc = null`) and bounded windows with UTC normalization assertions.
4. **Expected:** All tests pass; unresolved-state rows remain queryable for downstream diagnostics.

## Edge Cases

### Adjacent interval boundary continuity

1. Execute DbContext provenance boundary tests from `HappyGymStatsDbContextTests`.
2. Confirm adjacent windows where previous `ValidToUtc == next ValidFromUtc` persist without overlap corruption.
3. **Expected:** Persistence succeeds and tests pass, preserving deterministic interval boundary semantics.

## Failure Signals

- Any failing test asserting missing `ModifierProvenance` columns/indexes indicates schema drift.
- Constraint tests unexpectedly accepting invalid scope/status values indicate domain-guard regression.
- Interval round-trip failures indicate UTC conversion or null-bound handling regressions.

## Not Proven By This UAT

- Reconstruction pipeline consumption of provenance rows (planned for S03).
- API payload confidence scoring and frontend gradient rendering (planned for S04/S05).

## Notes for Tester

This UAT intentionally validates data-contract readiness, not end-user runtime behavior. If tests fail, inspect migration snapshot alignment and DbContext check-constraint/index definitions first; those are the highest-signal diagnostics for this slice.
