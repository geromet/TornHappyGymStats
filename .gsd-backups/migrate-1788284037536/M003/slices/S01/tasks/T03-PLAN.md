---
estimated_steps: 8
estimated_files: 5
skills_used: []
---

# T03: Expose surfaces cache readiness distinctly

Why: `/api/v1/torn/health` reports DB reachability, but the surfaces endpoint and cache path are the specific path Blazor needs; both need explicit diagnostics.

Do:
1. Review `HealthController`, `SurfacesController`, and `AppConfiguration.ResolveSurfacesCacheDirectory`.
2. Add a non-secret diagnostic surface or deploy check that confirms the resolved surfaces cache directory exists and whether `latest.json` is present.
3. Keep API response behavior compatible: missing surfaces data should remain structured 404 for `/surfaces/latest`, not a server error.
4. If health endpoint is extended, avoid leaking filesystem paths if that is considered sensitive; otherwise prefer script-side diagnostics.
5. Add tests or shell checks for missing cache behavior.

Done when: API health/deploy verification can separate “API up but no surfaces cache yet” from “API unreachable/502”.

## Inputs

- `src/HappyGymStats.Api/Controllers/SurfacesController.cs`
- `src/HappyGymStats.Api/Controllers/HealthController.cs`
- `scripts/verify/s05-local-surfaces.sh`

## Expected Output

- `scripts/deploy-backend.sh`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Verification

dotnet test --filter "ApiEndpointTests" && rg -n "surfaces|latest.json|not_found|cache" scripts/deploy-backend.sh src/HappyGymStats.Api tests/HappyGymStats.Tests/ApiEndpointTests.cs

## Observability Impact

Signals added/changed: no-cache/missing-latest distinguished from API-down.
How a future agent inspects this: call surfaces latest/meta and read deploy health output.
Failure state exposed: missing `latest.json`, missing cache directory, malformed health response.
