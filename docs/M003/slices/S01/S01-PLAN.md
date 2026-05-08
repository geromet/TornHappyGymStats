# S01: Prove API reachability and production config

**Goal:** Make the API production runtime contract explicit and verifiable before tackling the Blazor 502. This slice isolates whether 502 is caused by API process failure, bad systemd env, Postgres/container failure, bad nginx routing, or missing surfaces cache configuration.
**Demo:** API deploy fails fast if Postgres/env/service startup is broken, and `/api/v1/torn/health` can be verified through both loopback and nginx.

## Must-Haves

- API service file or associated environment contract declares required production settings: connection string, provisional token signing key, surfaces cache directory, ASPNETCORE_URLS, and production environment.
- Deploy/restart path verifies `systemctl status happygymstats-api` after restart and fails on inactive/failed states.
- Loopback health check to `http://127.0.0.1:5047/api/v1/torn/health` is part of deploy verification.
- External/nginx health check to `https://torn.geromet.com/api/v1/torn/health` is part of deploy verification.
- Failure output distinguishes service not running, port not listening, health endpoint non-2xx, database degraded/unreachable, and nginx 502.
- Surfaces cache directory contract is explicit and aligned with any nginx static cache route that remains supported.
- Secrets are referenced by env names/files only; no secret values are logged or committed.

## Proof Level

- This slice proves: Operational integration proof. Real runtime is required for full proof; local-safe command-level verification is acceptable during implementation, but the slice is not fully closed until loopback and nginx API health checks are defined and runnable.

## Integration Closure

Upstream surfaces consumed: `scripts/deploy-backend.sh`, `scripts/deploy-config.sh`, `infra/happygymstats-api.service`, `infra/nginx-torn.conf`, `src/HappyGymStats.Api/Program.cs`, `src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs`, `src/HappyGymStats.Api/Controllers/HealthController.cs`, `src/HappyGymStats.Api/Controllers/SurfacesController.cs`.
New wiring introduced: API deploy verification and production runtime env contract for service health, DB, and surfaces cache.
What remains before milestone end-to-end: Blazor must consume this boundary in S02; smoke script must aggregate it in S05; docs must capture it in S08.

## Verification

- Runtime signals: deploy phase output for API restart, service status, loopback health, external nginx health, DB status, and surfaces cache path.
- Inspection surfaces: `scripts/deploy-backend.sh`, any new API health verification helper, `/api/v1/torn/health`, systemd status, nginx route response.
- Failure visibility: failed phase, command, URL, HTTP status, and short body excerpt where safe.
- Redaction constraints: never print connection string values, token signing keys, Torn API keys, or environment file contents.

## Tasks

- [ ] **T01: Declare API production environment contract** `est:1h`
  Why: The API currently has placeholder settings and service env that do not declare the production contract, so deployment can fail into an nginx 502 without clear precondition output.
  - Files: `infra/happygymstats-api.service`, `scripts/deploy-backend.sh`, `scripts/deploy-config.sh`, `docs/DEPLOYMENT.md`, `src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs`
  - Verify: rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ConnectionStrings__HappyGymStats|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|ASPNETCORE_URLS" infra/happygymstats-api.service scripts/deploy-backend.sh docs/DEPLOYMENT.md && dotnet build

- [ ] **T02: Add backend deploy health gates** `est:1.5h`
  Why: The backend deploy currently restarts the API and stops, so a broken service is only discovered by the Blazor UI as 502.
  - Files: `scripts/deploy-backend.sh`, `scripts/deploy-config.sh`
  - Verify: bash -n scripts/deploy-backend.sh && bash -n scripts/deploy-config.sh && rg -n "health|is-active|systemctl|127.0.0.1:5047|torn.geromet.com/api/v1/torn/health|502" scripts/deploy-backend.sh scripts/deploy-config.sh

- [ ] **T03: Expose surfaces cache readiness distinctly** `est:1h`
  Why: `/api/v1/torn/health` reports DB reachability, but the surfaces endpoint and cache path are the specific path Blazor needs; both need explicit diagnostics.
  - Files: `src/HappyGymStats.Api/Controllers/HealthController.cs`, `src/HappyGymStats.Api/Controllers/SurfacesController.cs`, `src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs`, `scripts/deploy-backend.sh`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
  - Verify: dotnet test --filter "ApiEndpointTests" && rg -n "surfaces|latest.json|not_found|cache" scripts/deploy-backend.sh src/HappyGymStats.Api tests/HappyGymStats.Tests/ApiEndpointTests.cs

- [ ] **T04: Add S01 local contract verifier** `est:45m`
  Why: The slice needs one command that proves the API path locally before production credentials/server access are involved.
  - Files: `scripts/verify/s01-api-production-contract.sh`, `scripts/deploy-backend.sh`, `docs/DEPLOYMENT.md`
  - Verify: bash scripts/verify/s01-api-production-contract.sh

## Files Likely Touched

- infra/happygymstats-api.service
- scripts/deploy-backend.sh
- scripts/deploy-config.sh
- docs/DEPLOYMENT.md
- src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs
- src/HappyGymStats.Api/Controllers/HealthController.cs
- src/HappyGymStats.Api/Controllers/SurfacesController.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- scripts/verify/s01-api-production-contract.sh
