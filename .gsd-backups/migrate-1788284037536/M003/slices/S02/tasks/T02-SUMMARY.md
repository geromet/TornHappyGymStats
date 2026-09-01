---
id: T02
parent: S02
milestone: M003
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor
  - tests/HappyGymStats.Tests/SurfacesServiceFailureClassificationTests.cs
  - tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
key_decisions:
  - Standardized Blazor service error propagation on typed `ApiFailure` rather than raw `EnsureSuccessStatusCode` exceptions.
  - Kept 404 surfaces-cache miss as `null` return to preserve the existing no-data UI state while classifying other failures.
  - Used extern alias for Blazor test reference to avoid `Program` type collision with API test host.
duration: 
verification_result: passed
completed_at: 2026-05-06T19:32:21.110Z
blocker_discovered: false
---

# T02: Added typed ApiFailure classification in Blazor SurfacesService and updated Home UI/logging to show safe, category-aware API failures.

**Added typed ApiFailure classification in Blazor SurfacesService and updated Home UI/logging to show safe, category-aware API failures.**

## What Happened

Implemented a shared `ApiFailure` model (`ApiFailureCategory`, endpoint, optional HTTP status, safe message) for Blazor service calls. Refactored `SurfacesService.GetLatestAsync` and `StartImportAsync` to use one classification path: 404 on surfaces cache still returns `null`, non-success HTTP responses map through `ApiFailure.FromHttp`, and JSON decode failures map through `ApiFailure.Deserialization` so malformed payloads are distinct from HTTP failures. Added import-outcome classification (`Outcome == failed`) as `ImportFailure` with safe messaging. Updated `Home.razor` error handling to catch `ApiFailure`, display `SafeMessage`, and emit structured logs with endpoint/status/category while avoiding secret leakage. Added targeted tests in `SurfacesServiceFailureClassificationTests` to verify 404-null behavior, 502 classification, deserialization classification, and that API key values are never echoed in thrown messages.

## Verification

Ran the task verification command (`dotnet build` + classifier grep) and added/ran focused unit tests for `SurfacesService` failure classification and API-key redaction. Build succeeded after resolving a logger overload mismatch and rerunning sequentially to avoid parallel file-lock contention in Api runtimeconfig generation.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet build && rg -n "BadGateway|NotFound|ApiFailure|EnsureSuccessStatusCode|apiKey" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor` | 0 | ✅ pass | 5833ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter SurfacesServiceFailureClassificationTests` | 0 | ✅ pass | 5989ms |

## Deviations

Added focused unit tests under `tests/HappyGymStats.Tests` and an aliased Blazor project reference to validate classification behavior directly; this extends the plan’s verification depth but does not change functional scope.

## Known Issues

Repository-level build still reports pre-existing package vulnerability warnings (NU1903) and browser-platform crypto warning (CA1416), unchanged by this task.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`
- `tests/HappyGymStats.Tests/SurfacesServiceFailureClassificationTests.cs`
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
