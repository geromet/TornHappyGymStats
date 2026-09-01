# S02: S02 — UAT

**Milestone:** M001
**Written:** 2026-04-30T23:11:02.362Z

# S02: S02 — UAT

**Milestone:** M001  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice delivers backend durability and API contract behavior (not UI), and the required acceptance signals are fully represented by deterministic endpoint/integration tests over real SQLite persistence.

## Preconditions

- .NET 8 SDK installed.
- Project dependencies restored.
- Test host can create SQLite-backed test databases.
- Command available:
  - `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"`

## Smoke Test

Run the filtered test command and confirm all targeted tests pass; this validates that import status endpoints return durable run data and that restart-boundary retrieval works.

## Test Cases

### 1. Latest import status is read from durable run history

1. Seed an `ImportRuns` record in the test DB with a known run ID and lifecycle status.
2. Call `GET /v1/import/latest`.
3. **Expected:** Response is `200` with payload matching the seeded run identity and status fields sourced from DB-backed history.

### 2. Import run lookup by ID returns persisted record

1. Seed an `ImportRuns` row with a known ID.
2. Call `GET /v1/import/{id}` using that exact ID.
3. **Expected:** Response is `200` and returns the corresponding persisted run data.

### 3. Unknown import run ID returns standard error envelope

1. Call `GET /v1/import/{id}` with a non-existent ID.
2. **Expected:** Response is `404` with `not_found` error envelope shape.

### 4. Restart-boundary durability across fresh app instances

1. Seed file-backed SQLite with an import run.
2. Start first test app instance and call `GET /v1/import/{id}`; record response identity.
3. Dispose first instance, start a fresh second instance pointed at the same DB, and call the same endpoint.
4. **Expected:** Response in second instance still resolves the same run ID from DB; status remains within valid lifecycle progression set (`queued|running|completed|failed|cancelled`) even if async processing advanced state.

## Edge Cases

### Asynchronous state advancement during verification

1. Trigger/read an import run while background processing is active.
2. Read status again after short delay or from a fresh instance.
3. **Expected:** Run remains retrievable; state may move forward in lifecycle but must stay valid and durable.

## Failure Signals

- `GET /v1/import/latest` returns empty/incorrect data after restart despite seeded run history.
- `GET /v1/import/{id}` fails to find existing persisted run.
- Unknown ID does not return `404 not_found` envelope.
- Restart-boundary test fails to retrieve the same run identity from second app instance.

## Not Proven By This UAT

- Throughput/performance under large import volume (covered by later performance slices).
- Transactional derived dataset refresh consistency guarantees (covered in S03).

## Notes for Tester

- Lifecycle status can legitimately advance between reads due to asynchronous worker execution; treat durable retrievability and valid status progression as pass criteria, not a fixed terminal state at an exact instant.
