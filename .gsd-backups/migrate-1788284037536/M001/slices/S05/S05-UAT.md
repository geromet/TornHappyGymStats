# S05: S05 — UAT

**Milestone:** M001
**Written:** 2026-04-30T23:38:01.510Z

# S05: S05 — UAT

**Milestone:** M001  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice ships test-surface contract hardening (not a new user-facing runtime flow), and its acceptance criteria are explicitly encoded in deterministic DB-native integration and API tests.

## Preconditions

- .NET 8 SDK installed and `dotnet` available.
- Repository dependencies restored.
- Ability to run test project: `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`.

## Smoke Test

Run:

1. `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ExportedDatasetConsistencyTests|FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ApiEndpointTests"`
2. **Expected:** 0 failed tests and all targeted DB-native parity/API tests pass.

## Test Cases

### 1. DB-native import failure durability parity

1. Run filtered test target containing `ExportedDatasetConsistencyTests`.
2. Confirm test path drives a failing import via fetcher path and records outcome.
3. **Expected:** Assertions prove `ImportRuns`/`ImportCheckpoints` persist failure outcome, completion timestamp, and durable error message.

### 2. Reconstruction-to-derived row identity parity

1. Run filtered test target containing `ExportedDatasetConsistencyTests` and `DbPipelineIntegrationTests`.
2. Confirm test seeds raw logs into SQLite and executes production `ReconstructionRunner`.
3. **Expected:** Persisted `DerivedGymTrains` rows match reconstruction output values and derived happy events are materialized.

### 3. API import→reconstruct→read DB-native coherence

1. Run filtered test target containing `ApiEndpointTests`.
2. Confirm test seeds ImportRuns and RawUserLogs in temp SQLite, runs reconstruction, and calls:
   - `/v1/import/latest`
   - `/v1/import/{id}`
   - `/v1/gym-trains`
   - `/v1/happy-events`
3. **Expected:** Endpoints return coherent DB-backed contract data and derived reads remain available post-reconstruction without legacy export fixtures.

## Edge Cases

### In-memory lifecycle vs durable import row state

1. Execute API parity test under test host lifecycle.
2. Observe status behavior for `/v1/import/latest` and `/v1/import/{id}`.
3. **Expected:** Assertions tolerate lifecycle-reported running states while still requiring durable DB-backed availability/coherence.

## Failure Signals

- Any xUnit failure in `ExportedDatasetConsistencyTests`, `DbPipelineIntegrationTests`, or `ApiEndpointTests`.
- Missing persisted import failure metadata (outcome/timestamp/error text).
- Derived endpoint payload mismatch after reconstruction.
- Import status endpoints not returning durable run visibility by ID/latest contract.

## Not Proven By This UAT

- Production non-test-host runtime race behavior under concurrent live imports.
- Operational SLO characteristics (latency/throughput) beyond correctness of DB-native contract.

## Notes for Tester

Treat these tests as executable documentation for DB-native parity. The critical acceptance signal is contract coherence and durability; strict hard-coded latest outcome assumptions are intentionally avoided where in-memory lifecycle transitions can legitimately influence status representation.
