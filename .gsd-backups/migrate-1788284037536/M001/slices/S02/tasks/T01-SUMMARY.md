---
id: T01
parent: S02
milestone: M001
key_files:
  - src/HappyGymStats.Api/ImportService.cs
  - src/HappyGymStats.Api/Program.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-04-30T23:07:05.692Z
blocker_discovered: false
---

# T01: Persisted import lifecycle state in ImportRuns and exposed durable status reads via ImportService-backed /v1/import/latest and /v1/import/{id}.

**Persisted import lifecycle state in ImportRuns and exposed durable status reads via ImportService-backed /v1/import/latest and /v1/import/{id}.**

## What Happened

Implemented durable import run tracking in `ImportService` by creating an `ImportRuns` row at enqueue time (`queued`) and updating the same row through `running`, `completed`, `failed`, and `cancelled` transitions. Added DB-backed query methods `GetLatestAsync` and `GetByIdAsync` that map durable rows into `ImportJobStatus` consistently, while keeping API keys request-scoped and never persisted. Updated API endpoints to call these async service methods and added `/v1/import/{id}` for restart-safe historical lookup. Terminal-state updates on cancellation/error paths explicitly persist `CompletedAtUtc`, outcome, and error message.

## Verification

Ran the task verification command for API endpoint tests and confirmed the suite passes after the durability and endpoint changes.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests"` | 0 | ✅ pass | 1000ms |

## Deviations

Extended endpoint surface by adding `/v1/import/{id}` in this task to consume `GetByIdAsync` immediately; plan output listed only `ImportService.cs` but this endpoint wiring was required to expose the new query method.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Api/ImportService.cs`
- `src/HappyGymStats.Api/Program.cs`
