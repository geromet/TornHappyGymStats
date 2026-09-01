# S03: S03 — UAT

**Milestone:** M001
**Written:** 2026-04-30T23:17:20.402Z

# S03: S03 — UAT

**Milestone:** M001  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: The slice contract is data-consistency at transaction boundaries; deterministic integration/API tests against SQLite state and endpoint payloads directly prove the no-empty-window behavior.

## Preconditions

- .NET 8 SDK installed.
- Test project dependencies restored.
- Repository at a state containing S03 changes in `ReconstructionRunner` and integration tests.

## Smoke Test

Run:
1. `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`
2. **Expected:** Db pipeline tests pass, including the rollback scenario that injects failure between clear and insert.

## Test Cases

### 1. Transaction commit refreshes derived datasets atomically

1. Seed source logs into SQLite test DB.
2. Run reconstruction successfully.
3. Query derived tables (`DerivedGymTrains`, `DerivedHappyEvents`) via integration assertions.
4. **Expected:** Both tables contain refreshed committed rows together; no table is left in an intermediate state.

### 2. Failed refresh preserves last-good API responses

1. Seed source logs and run successful reconstruction to establish baseline derived rows.
2. Call `/v1/gym-trains` and `/v1/happy-events`; record returned item identities.
3. Trigger reconstruction with injected `beforeDerivedInsert` failure.
4. Re-call `/v1/gym-trains` and `/v1/happy-events`.
5. **Expected:** Post-failure identities match pre-failure baseline; endpoints do not expose empty/partial refresh output.

## Edge Cases

### Failure after clear intent but before insert

1. Use deterministic failure seam to throw after derived clear step begins and before insert executes.
2. Re-open DB assertions for derived row counts and API reads.
3. **Expected:** Transaction rollback keeps previously committed derived rows queryable.

## Failure Signals

- Any failing test in `DbPipelineIntegrationTests` or `ApiEndpointTests` related to rollback/no-empty-window.
- Endpoint payload identity mismatch before vs after injected failure.
- Derived table row counts unexpectedly drop to zero after failed refresh.

## Not Proven By This UAT

- Performance characteristics of reconstruction at large scale (handled in S04).
- Long-running operational alerting/telemetry around repeated reconstruction failures.

## Notes for Tester

The API regression test intentionally compares payload identities rather than asserting non-empty counts, because minimal seeds can validly produce an empty endpoint while still satisfying the no-empty-window contract. The key invariant is stability of last-good committed output across failed refresh attempts.
