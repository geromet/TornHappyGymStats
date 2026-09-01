---
id: T03
parent: S02
milestone: M004
key_files:
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-09T17:36:34.825Z
blocker_discovered: false
---

# T03: Stabilized My stats ownership regression coverage by fixing authenticated test-host state isolation and stale SQLite DbContext tests, then verified all S02 endpoint/service filters pass.

**Stabilized My stats ownership regression coverage by fixing authenticated test-host state isolation and stale SQLite DbContext tests, then verified all S02 endpoint/service filters pass.**

## What Happened

I first reproduced the reported Blazor verification failure and found the run was blocked by compile errors in `HappyGymStatsDbContextTests` from obsolete entity/DbContext contracts. I rewrote that test file to match the current schema/key contracts so filtered suites could compile and execute. Next, I reproduced the combined S02 filter and isolated failing API ownership tests to a test-host isolation bug: `CreateAuthenticatedClient` used `WithWebHostBuilder`, which created a distinct host with a different in-memory SQLite connection, so seeded identity/log rows were absent for authenticated requests. I moved test authentication registration into the primary factory `ConfigureWebHost` and changed `CreateAuthenticatedClient` to reuse `CreateClient` with headers. One deterministic failure remained (`/import-jobs/latest` not-found regression) due to orchestrator `Latest` state leaking across tests; I reset `_latest` during `ResetDatabase` via reflection in the test factory. After these targeted fixes, all required S02 verification commands passed, and endpoint scan confirms private My stats paths remain on `/me` endpoints with no fallback to public import/read routes.

## Verification

Executed required verification commands from the slice/task plan: targeted `SqliteApiEndpointTests`, `BlazorApiFailureTests`, combined `SqliteApiEndpointTests|BlazorApiFailureTests|SurfacesServiceFailureClassificationTests`, and static endpoint-path scan for My stats service/page. All commands exited 0. Combined suite reports 36/36 passing; Blazor failure suite reports 16/16 passing; sqlite endpoint suite reports 16/16 passing. Endpoint scan confirms `/api/v1/torn/import-jobs/me` and `/api/v1/torn/surfaces/me` usage in `SurfacesService` with no private routing through `/surfaces/latest` in My stats flow.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"` | 0 | ✅ pass | 14225ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"` | 0 | ✅ pass | 12078ms |
| 3 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"` | 0 | ✅ pass | 14147ms |
| 4 | `rg -n "/api/v1/torn/import-jobs/me|/api/v1/torn/surfaces/me|/api/v1/torn/surfaces/latest" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor` | 0 | ✅ pass | 12ms |

## Deviations

Updated `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` (not in expected-output list) because the test assembly could not compile due to stale schema contracts; this was required to run the task’s mandated filtered verification commands.

## Known Issues

None.

## Files Created/Modified

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`
