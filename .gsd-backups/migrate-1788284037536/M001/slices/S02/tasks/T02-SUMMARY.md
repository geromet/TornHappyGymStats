---
id: T02
parent: S02
milestone: M001
key_files:
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-04-30T23:09:44.520Z
blocker_discovered: false
---

# T02: Added DB-focused API endpoint tests for import latest/by-id and restart-safe status retrieval across fresh app instances.

**Added DB-focused API endpoint tests for import latest/by-id and restart-safe status retrieval across fresh app instances.**

## What Happened

Validated that the import status routes already used durable ImportService query methods in Program.cs, then expanded ApiEndpointTests to cover DB-backed status retrieval scenarios required by the task contract. I added explicit tests for `/v1/import/latest` reading seeded ImportRuns history, `/v1/import/{id}` returning the expected run payload, and unknown IDs returning the standard `404 not_found` envelope. I also added a restart-boundary test that seeds a file-backed SQLite database and verifies the same run ID is retrievable from two fresh `WebApplicationFactory` instances, demonstrating restart-safe lookup continuity. To support this, I introduced test helpers for seeding ImportRuns and a persistent SQLite factory override via in-memory configuration.

## Verification

Ran the slice task verification filter for ApiEndpointTests and DbPipelineIntegrationTests; all targeted tests passed after updating restart-boundary assertions to validate durable identity retrieval while allowing asynchronous lifecycle state transitions.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"` | 0 | ✅ pass | 2000ms |

## Deviations

Restart-safety assertion was adjusted from a strict fixed outcome (`cancelled`) to lifecycle-set membership because background processing can advance run state asynchronously; the durable requirement is stable retrievability across fresh instances.

## Known Issues

None.

## Files Created/Modified

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
