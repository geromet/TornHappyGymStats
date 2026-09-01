# S01: S01

**Goal:** Deliver My stats page end-to-end with claim-bound API and auth-safe UI behavior.
**Demo:** Signed-in user opens /my-stats, sees only their gym point cloud; signed-out users are challenged; endpoint is claim-bound with no PlayerID input.

## Must-Haves

- `/api/v1/torn/surfaces/me` exists, is `[Authorize(Roles=Roles.User)]`, and resolves caller via anonymous_id claim only.
- API response shape supports Blazor point cloud rendering and excludes direct PlayerID exposure.
- `/my-stats` page is auth-protected, visible in menu with lock indicator, and renders user-only point cloud.
- PLAN includes an explicit operator gate for manual Keycloak changes with pause/resume criteria.

## Proof Level

- This slice proves: Executable verification via targeted and full build/test/smoke commands.

## Integration Closure

Endpoint, service DTOs, page route, nav link, and verification scripts all align on anonymous_id claim-bound access.

## Verification

- Ensure failures are classifiable (401/403/not_found/transport) and logged without sensitive values.

## Tasks

- [x] **T01: Added authenticated claim-bound GET /api/v1/torn/surfaces/me and caller-scoped gym log retrieval with unauthorized handling for missing/invalid anonymous_id claims.** `est:25m`
  Implement authenticated `GET /api/v1/torn/surfaces/me` in API. Resolve caller anonymous_id claim, return 401 when claim missing/invalid, and project only caller gym rows into chart payload shape. Extend repository contracts/implementation as needed for caller-scoped gym cloud retrieval. Keep route claim-bound and do not accept PlayerID/user id inputs.
  - Files: `src/HappyGymStats.Api/Controllers/SurfacesController.cs`, `src/HappyGymStats.Contracts/Repositories/IUserLogEntryRepository.cs`, `src/HappyGymStats.Data/Repositories/UserLogEntryRepository.cs`, `src/HappyGymStats.Core/Services/GymTrainsService.cs`
  - Verify: dotnet build src/HappyGymStats.Api/HappyGymStats.Api.csproj && dotnet test --filter "FullyQualifiedName~Api|FullyQualifiedName~Identity|FullyQualifiedName~GymTrains"

- [x] **T02: Extended Blazor surfaces DTOs and SurfacesService with typed /surfaces/me support using existing ApiFailure classification behavior.** `est:15m`
  Add Blazor DTO/service support for My stats endpoint. Extend Surfaces DTO models and SurfacesService with GetMyStatsAsync using existing ApiFailure classification conventions.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
  - Verify: dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj

- [x] **T03: Added an auth-protected /my-stats page with claim-bound chart loading states and wired a locked My stats nav link.** `est:25m`
  Create new auth-protected `/my-stats` page that renders a point cloud like Home but sourced from GetMyStatsAsync, with empty/loading/error states. Update main nav menu to include My stats with lock icon indicator.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`
  - Verify: dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj

- [x] **T04: Enforced Roles.User on claim-bound /surfaces/me and upgraded the S02 verifier with explicit Keycloak operator-gate pause/resume checks while capturing current full-suite gate failures.** `est:20m`
  Run end-to-end verification and enforce Keycloak operator gate. Confirm signed-out auth behavior, signed-in data rendering, claim-bound endpoint behavior, and include/manual gate instructions for pausing auto-mode when Keycloak config changes are required.
  - Files: `.gsd/workflows/features/260509-2-add-a-my-stats-page-to-the-blazor-projec/PLAN.md`, `scripts/verify/s02-blazor-api-boundary.sh`
  - Verify: dotnet build HappyGymStats.sln && dotnet test && scripts/verify/s02-blazor-api-boundary.sh

- [ ] **T05: Fix verification drift and rerun slice gates** `est:25m`
  Resolve the current slice-level verification failures in an execution-capable unit. Update Blazor DTO/test expectations so SurfacesDatasetMetaDto supports the latest-surface provenance diagnostics contract, update stale HappyGymStatsDbContextTests to the current UserLogEntries/ModifierProvenance schema, ensure API auth tests include the Roles.User claim required by /api/v1/torn/surfaces/me, then rerun all slice gates. Do not weaken the claim-bound /surfaces/me behavior or add PlayerID/user id inputs.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`, `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`, `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
  - Verify: dotnet build HappyGymStats.sln && dotnet test && scripts/verify/s02-blazor-api-boundary.sh

## Files Likely Touched

- src/HappyGymStats.Api/Controllers/SurfacesController.cs
- src/HappyGymStats.Contracts/Repositories/IUserLogEntryRepository.cs
- src/HappyGymStats.Data/Repositories/UserLogEntryRepository.cs
- src/HappyGymStats.Core/Services/GymTrainsService.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor
- .gsd/workflows/features/260509-2-add-a-my-stats-page-to-the-blazor-projec/PLAN.md
- scripts/verify/s02-blazor-api-boundary.sh
- tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
- tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
