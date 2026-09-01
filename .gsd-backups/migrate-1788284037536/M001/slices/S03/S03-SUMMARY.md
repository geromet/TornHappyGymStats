---
id: S03
parent: M001
milestone: M001
provides:
  - Atomic derived dataset refresh semantics that preserve last-good committed reads across failed reconstruction attempts.
requires:
  []
affects:
  - S04
  - S05
key_files:
  - src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
key_decisions:
  - Use a single DB transaction for derived-table clear+insert to remove empty/partial read windows.
  - Use a constructor-level failure seam (`beforeDerivedInsert`) for deterministic rollback verification without altering production control flow.
  - Validate API no-empty-window behavior by pre/post identity stability instead of brittle fixed cardinality assertions.
patterns_established:
  - Transactional dataset swap pattern for derived-table refresh.
  - Deterministic failure injection seam for integration rollback tests.
  - Consumer-boundary regression testing by comparing stable response identities across failure events.
observability_surfaces:
  - DB-first diagnostics through ImportRuns outcome/error and direct SQLite derived-table row-count assertions during rollback scenarios.
drill_down_paths:
  - .gsd/milestones/M001/slices/S03/tasks/T01-SUMMARY.md
  - .gsd/milestones/M001/slices/S03/tasks/T02-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-04-30T23:17:20.402Z
blocker_discovered: false
---

# S03: S03

**Shipped atomic transactional derived-table refresh plus regression coverage proving API readers keep last-good derived data when reconstruction fails mid-refresh.**

## What Happened

S03 closed the empty-window risk in reconstruction by moving derived-table refresh into a single DB transaction in ReconstructionRunner, covering both DerivedGymTrains and DerivedHappyEvents as one all-or-nothing unit. T01 introduced this transactional boundary and added a deterministic failure seam (`beforeDerivedInsert`) so tests can force a failure exactly after clear intent but before insert. Integration tests then proved rollback preserves previously committed derived rows. T02 extended consumer-facing protection with API regression coverage: tests establish a last-good dataset, trigger a failing refresh, and verify `/v1/gym-trains` and `/v1/happy-events` still return the pre-failure identities rather than empty/partial results. Together these changes preserve endpoint contract stability without changing API surface.

## Verification

Executed slice-level verification command from the plan: `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"` and confirmed pass (15/15). This covers transactional commit behavior, rollback preservation in DB pipeline integration tests, and API-level no-empty-window regression assertions.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

T02 adjusted assertion strategy from simple non-empty checks to pre/post identity equality for endpoint payloads to make the contract test robust for minimal datasets.

## Known Limitations

Operational observability for reconstruction failure trends (alerts/metrics) is still limited; this slice focuses on transactional correctness and endpoint consistency, not monitoring infrastructure.

## Follow-ups

S04 should benchmark reconstruction duration/scale effects now that atomic refresh semantics are in place. S05 should extend DB-native parity coverage to full import→reconstruct→read flow scenarios beyond rollback contract checks.

## Files Created/Modified

- `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs` — Wrapped derived-table refresh in a single transaction and added deterministic pre-insert failure seam for rollback testing.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — Added integration coverage proving rollback preserves previously committed derived rows after injected mid-refresh failure.
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs` — Added regression test asserting `/v1/gym-trains` and `/v1/happy-events` keep pre-failure identities when reconstruction refresh fails.
