---
estimated_steps: 1
estimated_files: 2
skills_used: []
---

# T02: Extended Blazor surfaces DTOs and SurfacesService with typed /surfaces/me support using existing ApiFailure classification behavior.

Add Blazor DTO/service support for My stats endpoint. Extend Surfaces DTO models and SurfacesService with GetMyStatsAsync using existing ApiFailure classification conventions.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`

## Expected Output

- `Blazor service method for /surfaces/me`
- `DTO types for my-stats chart payload`

## Verification

dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj

## Observability Impact

Failure categories remain consistent for My stats endpoint calls.
