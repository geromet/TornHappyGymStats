# S08: Refactor docs and API examples into current-state contract

**Goal:** Remove stale operational guidance left over from the static frontend/SQLite shape. Treat docs and API examples as executable-ish contracts for future agents and operators.
**Demo:** README, docs, and `.http` examples match the actual Blazor + API + Postgres + Keycloak + deployment shape.

## Must-Haves

- `README.md` describes current projects, Blazor frontend, API, Postgres, Keycloak, AdminPanel, and verification commands accurately.
- `docs/OVERVIEW.md` describes the current architecture and data flow without stale SQLite/static-dashboard claims unless explicitly labeled legacy.
- `docs/SETUP.md` describes current local setup, required environment variables, Postgres/container assumptions, and how to run API/Blazor/AdminPanel locally.
- `docs/DEPLOYMENT.md` describes current deploy roots, services, nginx routes, one-time setup vs steady-state deploy, and smoke script usage.
- `src/HappyGymStats.Api/HappyGymStats.Api.http` uses current `/api/v1/torn/...` routes.
- Docs link to the audit report copied under `docs/` or summarize it as the source for this milestone.
- Documentation changes are verified against real file paths and runnable commands.

## Proof Level

- This slice proves: Documentation contract proof. Verify paths and commands mechanically where possible; manual prose review still needed for clarity.

## Integration Closure

Upstream surfaces consumed: S01 API config/health, S02 Blazor API boundary, S03 AdminPanel setup, S04 AdminPanel route, audit report, current docs and `.http` examples.
New wiring introduced: current-state documentation contract and runnable API examples.
What remains before milestone end-to-end: S09 may add runtime/package details; otherwise docs become the stable reference.

## Verification

- Runtime signals: docs point operators to health/smoke commands and current endpoints.
- Inspection surfaces: README, docs, API `.http` file, optional docs drift verifier.
- Failure visibility: stale route/path/service names caught by marker checks.
- Redaction constraints: docs reference env var names and secret handling procedures only, never secret values.

## Tasks

- [ ] **T01: Update README current-state summary** `est:45m`
  Why: README is the first context future agents read; it currently points to stale SQLite/static dashboard assumptions.
  - Files: `README.md`
  - Verify: rg -n "Blazor|Postgres|Keycloak|AdminPanel|production-smoke|2026-05-06-181943" README.md && ! rg -n "stores the result in SQLite|static dashboard" README.md

- [ ] **T02: Refresh architecture overview** `est:1h`
  Why: Overview should explain the architecture accurately enough to plan work without rediscovering the refactor.
  - Files: `docs/OVERVIEW.md`, `HappyGymStats.sln`
  - Verify: rg -n "Blazor|PostgreSQL|Postgres|Keycloak|AdminPanel|surfaces|127.0.0.1:5047|127.0.0.1:5182|127.0.0.1:5048" docs/OVERVIEW.md && ! rg -n "SQLite storage model|static dashboard" docs/OVERVIEW.md

- [ ] **T03: Rewrite setup and deployment docs** `est:2h`
  Why: Setup and deployment docs are the operational contract for the failure class reported by the user.
  - Files: `docs/SETUP.md`, `docs/DEPLOYMENT.md`
  - Verify: rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|production-smoke|setup-adminpanel-server|happygymstats-api|happygymstats-blazor|happygymstats-adminpanel|--no-launch-profile" docs/SETUP.md docs/DEPLOYMENT.md

- [ ] **T04: Update API HTTP examples** `est:45m`
  Why: `.http` examples are runnable documentation and currently use stale routes.
  - Files: `src/HappyGymStats.Api/HappyGymStats.Api.http`, `src/HappyGymStats.Api/Controllers`
  - Verify: rg -n "/api/v1/torn/health|/api/v1/torn/surfaces/latest|/api/v1/torn/import-jobs" src/HappyGymStats.Api/HappyGymStats.Api.http && ! rg -n "GET .* /v1/|POST .* /v1/|localhost:5047/v1" src/HappyGymStats.Api/HappyGymStats.Api.http

- [ ] **T05: Add docs current-state drift verifier** `est:1h`
  Why: Documentation drift caused part of this audit. A lightweight verifier should catch stale route/service/path claims.
  - Files: `scripts/verify/s08-docs-contract.sh`, `README.md`, `docs/OVERVIEW.md`, `docs/SETUP.md`, `docs/DEPLOYMENT.md`, `src/HappyGymStats.Api/HappyGymStats.Api.http`
  - Verify: bash scripts/verify/s08-docs-contract.sh

## Files Likely Touched

- README.md
- docs/OVERVIEW.md
- HappyGymStats.sln
- docs/SETUP.md
- docs/DEPLOYMENT.md
- src/HappyGymStats.Api/HappyGymStats.Api.http
- src/HappyGymStats.Api/Controllers
- scripts/verify/s08-docs-contract.sh
