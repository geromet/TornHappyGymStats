---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T01: Persist import job lifecycle updates into ImportRuns and expose query methods in ImportService

Implement durable import run tracking in `ImportService` so lifecycle transitions are persisted, not only stored in `_latest` memory. Keep API key ephemeral and never persisted. Add service query methods used by endpoints (`GetLatestAsync`, `GetByIdAsync`) that read from durable run rows and map to `ImportJobStatus` consistently. Include failure-safe update behavior for cancellation/error paths so terminal state and timestamps are always written.

## Inputs

- ``src/HappyGymStats.Api/ImportService.cs``
- ``src/HappyGymStats.Data/Entities/ImportRunEntity.cs``
- ``src/HappyGymStats.Data/HappyGymStatsDbContext.cs``
- ``src/HappyGymStats.Core/Fetch/LogFetcher.cs``

## Expected Output

- ``src/HappyGymStats.Api/ImportService.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests"
