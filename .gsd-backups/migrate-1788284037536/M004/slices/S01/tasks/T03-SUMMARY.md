---
id: T03
parent: S01
milestone: M004
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor
key_decisions:
  - Reused the established ApiFailure category pattern for user-safe UI error copy and structured logs instead of introducing new enum categories in the page layer.
duration: 
verification_result: mixed
completed_at: 2026-05-09T17:03:43.915Z
blocker_discovered: false
---

# T03: Added an auth-protected /my-stats page with claim-bound chart loading states and wired a locked My stats nav link.

**Added an auth-protected /my-stats page with claim-bound chart loading states and wired a locked My stats nav link.**

## What Happened

Implemented a new Blazor page at `Components/Pages/MyStats.razor` with `@page "/my-stats"` and `[Authorize]` so access is auth-protected by route policy. The page mirrors Home’s point-cloud rendering pattern but sources data from `SurfacesService.GetMyStatsAsync()` (claim-bound `/api/v1/torn/surfaces/me`) and provides explicit loading, empty/not-found, and failure states. For failure handling, I reused the existing ApiFailure classification conventions from prior tasks and mapped category-based user-safe messages while logging structured diagnostics (endpoint/status/category) without exposing secrets. I also updated `Components/Layout/MainLayout.razor` to add a `My stats` navigation item with a lock icon indicator, consistent with other auth-required links.

## Verification

Ran the task contract verification command for the Blazor app. Initial build failed due to a local mismatch (`ApiFailureCategory` does not define Unauthorized/Forbidden), then I aligned the new page’s failure mapping to existing enum categories and reran the build successfully with zero warnings/errors.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj` | 1 | ❌ fail | 4649ms |
| 2 | `dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj` | 0 | ✅ pass | 3757ms |

## Deviations

Minor local adaptation: instead of using Unauthorized/Forbidden-specific UI category branches, used the existing shared `ApiFailureCategory.Validation` branch because the current service enum does not expose distinct Unauthorized/Forbidden categories.

## Known Issues

No automated UI test project exists in this repository yet, so verification for this task is compile-time/build validation rather than component-level test execution.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`
