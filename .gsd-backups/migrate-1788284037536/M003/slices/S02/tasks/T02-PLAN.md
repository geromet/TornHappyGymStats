---
estimated_steps: 8
estimated_files: 3
skills_used: []
---

# T02: Classify API failures in SurfacesService

Why: Raw `EnsureSuccessStatusCode` exceptions produce opaque UI messages and hide the difference between bad gateway, no data, validation failure, and malformed backend responses.

Do:
1. Introduce a small Blazor service result/error type or exception type that carries endpoint, status code, category, and safe message.
2. Update `SurfacesService.GetLatestAsync` and `StartImportAsync` to use the classifier.
3. Preserve `null` or a clear typed result for 404 no-cache where the UI already has a no-data state.
4. Ensure request body values, especially `apiKey`, are never logged or included in thrown messages.
5. Keep JSON deserialization failures distinct from HTTP failures.

Done when: both surfaces load and import use one classification path and no longer display raw `Response status code does not indicate success` text.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`

## Verification

dotnet build && rg -n "BadGateway|NotFound|ApiFailure|EnsureSuccessStatusCode|apiKey" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor

## Observability Impact

Signals added/changed: endpoint/status/category carried in safe failures.
How a future agent inspects this: read UI alert text and server logs; test classifier.
Failure state exposed: 502 vs 404 vs validation/import vs deserialization.
