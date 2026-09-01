---
id: T02
parent: S02
milestone: M004
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
  - tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-09T17:31:21.879Z
blocker_discovered: false
---

# T02: Added authenticated My stats import UI/service flow via /api/v1/torn/import-jobs/me with redacted typed failures and endpoint-focused Blazor service tests.

**Added authenticated My stats import UI/service flow via /api/v1/torn/import-jobs/me with redacted typed failures and endpoint-focused Blazor service tests.**

## What Happened

Implemented the T02 contract across Blazor service, API failure typing, My stats page UI, and Blazor service tests. In SurfacesService, I added StartMyStatsImportAsync targeting /api/v1/torn/import-jobs/me and kept payload ownership-free (apiKey + fresh only), while preserving the existing global StartImportAsync path. I factored import status parsing so failed outcomes are normalized into ApiFailure ImportFailure without leaking secrets. In ApiFailure, I added Unauthorized, Forbidden, and IdentitySetupRequired categories and mapped /import-jobs/me-specific 404 plus 409 into identity setup blockers. In MyStats.razor, I added a visible import action (key input + disabled while importing), safe status messaging, endpoint/status/category logging, and post-queue reload through LoadSurfacesAsync/GetMyStatsAsync rather than global latest endpoints. In BlazorApiFailureTests, I replaced legacy import assertions with /me endpoint tests that assert body shape excludes anonymousId/playerId/owner fields, validate typed classification for 401/403/404/409/422/502 paths, verify invalid JSON and failed outcome handling, and preserve global StartImport happy-path coverage.

## Verification

Ran the requested BlazorApiFailureTests command and the broader SqliteApiEndpointTests gate command; both currently fail at test-project compile stage due pre-existing unrelated HappyGymStatsDbContextTests API/entity drift (RawUserLogs/DerivedGymTrains/ModifierProvenanceEntity members), not due T02 changes. Confirmed T02 code-path presence and endpoint wiring via ripgrep, and confirmed the touched runtime project compiles cleanly with dotnet build on HappyGymStats.Blazor.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"` | 1 | ❌ fail | 4985ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"` | 1 | ❌ fail | 5571ms |
| 3 | `dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj` | 0 | ✅ pass | 3202ms |
| 4 | `rg -n "StartMyStatsImportAsync|/api/v1/torn/import-jobs/me|my-gym-cloud-chart" src/HappyGymStats.Blazor tests/HappyGymStats.Tests` | 0 | ✅ pass | 41ms |

## Deviations

The plan expected dotnet test --filter FullyQualifiedName~BlazorApiFailureTests to run to completion; execution is currently blocked by unrelated compile failures in tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs, so I validated the task-specific implementation with targeted grep plus successful Blazor project build.

## Known Issues

tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj currently fails compilation because HappyGymStatsDbContextTests references removed/renamed DbContext sets and ModifierProvenanceEntity fields (e.g., RawUserLogs, DerivedGymTrains, DerivedGymTrainLogId). This prevents any filtered test run from executing until that unrelated test file is reconciled.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
