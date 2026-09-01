---
id: T01
parent: S02
milestone: M004
key_files:
  - src/HappyGymStats.Api/Controllers/ImportController.cs
  - src/HappyGymStats.Core/Import/ImportOrchestrator.cs
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-09T17:26:20.612Z
blocker_discovered: false
---

# T01: Added authenticated POST /api/v1/torn/import-jobs/me that claim-binds import ownership to the caller’s identity map and ignores client ownership tampering, with endpoint tests for caller binding and rejection paths.

**Added authenticated POST /api/v1/torn/import-jobs/me that claim-binds import ownership to the caller’s identity map and ignores client ownership tampering, with endpoint tests for caller binding and rejection paths.**

## What Happened

Implemented a new additive authenticated import path in `ImportController` at `POST /api/v1/torn/import-jobs/me` with `[Authorize(Roles = Roles.User)]`. The endpoint now resolves `anonymous_id` and `ClaimTypes.NameIdentifier` from claims, loads the identity-map row by caller anonymousId, rejects invalid/missing claim as 401, missing map as safe setup-blocking 409 (`identity_setup_required`), and keycloak subject mismatch as 403 before enqueue. It does not read ownership from request body fields. Added safe structured logs for accepted/rejected authenticated import outcomes (endpoint/code/status/jobId/anonymousId) without exposing Torn API key material. In `ImportOrchestrator`, added `EnqueueForAnonymousId` plus shared internal enqueue path to preserve existing public enqueue behavior while enabling explicit caller-owned anonymousId enqueue for authenticated imports. Extended `SqliteApiEndpointTests` with authenticated `/me` cases for invalid claim, missing map, subject mismatch, and body tampering ignored; tampering test verifies `ImportOrchestrator.Latest.AnonymousId` equals caller anonymousId.

## Verification

Ran the task verification command and slice verification commands. All `dotnet test ...HappyGymStats.Tests.csproj --filter ...` commands failed before test execution due to pre-existing compile errors in unrelated baseline tests (`HappyGymStatsDbContextTests` and `BlazorApiFailureTests`) referencing removed/mismatched members. Independently verified changed runtime code compiles via `dotnet build src/HappyGymStats.Api/HappyGymStats.Api.csproj` (pass, 0 errors). Verified slice route-reference grep command passes and confirms expected Blazor surface endpoints remain present.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"` | 1 | ❌ fail | 7480ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"` | 1 | ❌ fail | 4962ms |
| 3 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"` | 1 | ❌ fail | 5017ms |
| 4 | `rg -n "/api/v1/torn/import-jobs/me|/api/v1/torn/surfaces/me|/api/v1/torn/surfaces/latest" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor` | 0 | ✅ pass | 12ms |
| 5 | `dotnet build src/HappyGymStats.Api/HappyGymStats.Api.csproj` | 0 | ✅ pass | 2957ms |

## Deviations

Added test-host auth flexibility (`X-Test-Subject` + explicit role claim) and identity-map seeding/reset helpers in `ApiEndpointTests.cs` to support new `/me` ownership-contract test scenarios. Also chose 409 + `identity_setup_required` for missing identity map as the safe setup/blocking path.

## Known Issues

`tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` currently has unrelated compile-time failures that prevent filtered test execution; this blocks full verification pass of the requested test commands until upstream test baseline issues are repaired.

## Files Created/Modified

- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
