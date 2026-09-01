---
id: T02
parent: S01
milestone: M004
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
key_decisions:
  - Use a dedicated MyStatsDatasetDto/MyStatsSeriesDto/MyStatsMetaDto model set for /surfaces/me while reusing GymCloudSeriesDto for chart point compatibility.
  - Preserve existing SurfacesService failure handling conventions (404->null, EnsureSuccessOrThrow, ReadJsonOrThrowAsync) to keep ApiFailure category behavior stable across endpoints.
duration: 
verification_result: passed
completed_at: 2026-05-09T16:58:06.751Z
blocker_discovered: false
---

# T02: Extended Blazor surfaces DTOs and SurfacesService with typed /surfaces/me support using existing ApiFailure classification behavior.

**Extended Blazor surfaces DTOs and SurfacesService with typed /surfaces/me support using existing ApiFailure classification behavior.**

## What Happened

I added My stats DTO records in the Blazor surfaces model file and introduced a new SurfacesService.GetMyStatsAsync method that calls /api/v1/torn/surfaces/me. The method mirrors existing latest-surface call behavior: it returns null on 404, routes non-success statuses through EnsureSuccessOrThrow for consistent ApiFailure categorization, and parses payloads through ReadJsonOrThrowAsync so malformed responses are consistently surfaced as Deserialization failures. This keeps endpoint/status/category observability semantics aligned with existing service methods while adding the new auth-scoped data path.

## Verification

Ran the task verification command: dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj. Build succeeded with 0 warnings and 0 errors.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj` | 0 | ✅ pass | 8580ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
