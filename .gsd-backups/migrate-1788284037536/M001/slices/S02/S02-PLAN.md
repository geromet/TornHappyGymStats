# S02: Durable import/reconstruction run state + status endpoints

**Goal:** Make import/reconstruction job status durable in SQLite and expose restart-safe status retrieval through API endpoints.
**Demo:** After restarting API, /v1/import/latest and /v1/import/{id} still show accurate run history from DB.

## Must-Haves

- Import requests persist run records that survive API process restart.
- `GET /v1/import/latest` reads from durable run history when in-memory state is empty.
- `GET /v1/import/{id}` returns a specific persisted run or a standard `not_found` envelope.
- API tests prove status continuity across service-instance boundaries and cover missing-id failure path.

## Proof Level

- This slice proves: integration

## Integration Closure

Connect API status endpoints to `ImportRuns`/`ImportCheckpoints` in the shared DB context rather than volatile `ImportService` memory only, while preserving existing enqueue semantics and error envelope shape.

## Verification

- Adds durable inspection of import run lifecycle (`queued|running|completed|failed|cancelled`) via DB-backed endpoints and makes post-restart diagnosis possible from API surfaces without attaching to prior process memory.

## Tasks

- [x] **T01: Persist import job lifecycle updates into ImportRuns and expose query methods in ImportService** `est:1.5h`
  Implement durable import run tracking in `ImportService` so lifecycle transitions are persisted, not only stored in `_latest` memory. Keep API key ephemeral and never persisted. Add service query methods used by endpoints (`GetLatestAsync`, `GetByIdAsync`) that read from durable run rows and map to `ImportJobStatus` consistently. Include failure-safe update behavior for cancellation/error paths so terminal state and timestamps are always written.
  - Files: `src/HappyGymStats.Api/ImportService.cs`, `src/HappyGymStats.Data/Entities/ImportRunEntity.cs`, `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests"

- [x] **T02: Wire DB-backed status endpoints and add restart-safe API endpoint coverage** `est:1.5h`
  Update import status routes to use new durable query methods and add `GET /v1/import/{id}` with standard error envelope semantics. Extend API tests to verify: (1) latest status is retrievable from DB-backed history, (2) specific run lookup by id works, (3) unknown id returns `404 not_found`, and (4) status remains queryable after constructing a fresh test client/service instance to simulate restart boundary.
  - Files: `src/HappyGymStats.Api/Program.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"

## Files Likely Touched

- src/HappyGymStats.Api/ImportService.cs
- src/HappyGymStats.Data/Entities/ImportRunEntity.cs
- src/HappyGymStats.Data/HappyGymStatsDbContext.cs
- src/HappyGymStats.Api/Program.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
