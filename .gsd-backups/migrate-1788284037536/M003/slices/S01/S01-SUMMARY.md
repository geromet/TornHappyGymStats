---
id: S01
parent: M003
milestone: M003
provides:
  - Verified production API runtime contract for required env vars, service URL, Postgres/database health assumptions, surfaces cache directory, and health check URLs.
  - Deploy-time health gate pattern for API restart and failure categorization.
  - Stable loopback and nginx API health commands and expected response semantics for S02/S05.
  - Local S01 verifier command for future drift detection.
requires:
  []
affects:
  - S02
  - S05
  - S07
  - S08
key_files:
  - infra/happygymstats-api.service
  - scripts/deploy-backend.sh
  - scripts/deploy-config.sh
  - docs/DEPLOYMENT.md
  - src/HappyGymStats.Api/Program.cs
  - src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs
  - src/HappyGymStats.Api/Controllers/HealthController.cs
  - src/HappyGymStats.Api/Controllers/SurfacesController.cs
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - scripts/verify/s01-api-production-contract.sh
  - .gsd/PROJECT.md
key_decisions:
  - Kept the API health payload stable and implemented surfaces cache readiness as deploy-side probes to avoid exposing filesystem cache paths.
  - Treated structured `/api/v1/torn/surfaces/latest` 404 `not_found` as a distinct cache readiness warning rather than a generic API failure.
  - Made the S01 verifier local by default and remote URL checks opt-in via `S01_ALLOW_REMOTE_URL_CHECKS=1`.
  - Made API startup database initialization environment-aware: Testing uses `EnsureCreatedAsync`; non-testing environments use migrations.
patterns_established:
  - Deploy scripts emit machine-checkable `DEPLOY_PRECHECK_FAIL`, `DEPLOY_HEALTH_FAIL`, `DEPLOY_HEALTH_WARN`, and `DEPLOY_HEALTH_OK` markers with categories and safe excerpts.
  - Production runtime contracts are documented in systemd, deploy scripts, and deployment docs by env var name only, never by secret value.
  - Local slice contract verification combines static route/config anchors, syntax checks, docs anchors, and targeted endpoint tests.
  - Remote/live checks are opt-in for local verifiers so contract drift can be tested without production credentials.
observability_surfaces:
  - Backend deploy precheck markers for missing env file/key names.
  - Backend deploy health markers for systemd state, loopback API health, external nginx API health, nginx 502, and surfaces cache readiness.
  - `/api/v1/torn/health` loopback and external nginx URLs as authoritative health probes.
  - Surfaces latest/meta probes with distinct structured `not_found` handling.
  - `scripts/verify/s01-api-production-contract.sh` as the deterministic local diagnostic command.
drill_down_paths:
  - .gsd/milestones/M003/slices/S01/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S01/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S01/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S01/tasks/T04-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-06T19:23:23.781Z
blocker_discovered: false
---

# S01: Prove API reachability and production config

**Production API deploys now have an explicit runtime contract, categorized health gates, surfaces cache diagnostics, and a deterministic local verifier for loopback/nginx health readiness.**

## What Happened

S01 converted the API deployment path from a publish-and-restart flow into an explicit operational contract. The systemd unit, backend deploy script, deployment config, and deployment docs now agree that production loads `/etc/happygymstats/api.env` and requires the connection string alias, provisional token signing key, surfaces cache directory, `ASPNETCORE_URLS`, and production environment before deployment proceeds. The backend deploy script now performs preflight checks before publish/restart and uses grep-friendly failure markers without logging secret values.

After restart, the deploy path now runs backend health gates. It checks `systemctl is-active happygymstats-api`, prints bounded `systemctl status` context on failure, probes loopback API health at `http://127.0.0.1:5047/api/v1/torn/health`, and probes external nginx API health at `https://torn.geromet.com/api/v1/torn/health`. Failures are categorized so operators and future smoke scripts can distinguish service inactive, port/listener or loopback failure, non-2xx health, database-degraded health response, and a dedicated external nginx 502 class.

The slice also added deploy-time surfaces cache readiness diagnostics for the Blazor-critical `/api/v1/torn/surfaces/*` paths. Rather than exposing filesystem paths in the health API payload, the deploy script probes the surfaces metadata/latest routes and treats structured `404 not_found` for missing `latest.json` as `DEPLOY_HEALTH_WARN: category=surfaces_latest_missing`, separating cache-not-ready from API-down or nginx-bad-gateway failures.

Finally, S01 introduced `scripts/verify/s01-api-production-contract.sh` as the local deterministic contract gate. It checks script syntax, deploy token/category anchors, route anchors, docs anchors, the `--no-launch-profile` guard for pinned `ASPNETCORE_URLS`, and targeted `ApiEndpointTests`. To make that verifier meaningful, the test host was stabilized by removing mixed EF provider registrations in `ApiEndpointTests`, using the `Testing` environment, and making API startup use `EnsureCreatedAsync` only for Testing while preserving migrations for non-testing environments.

## Verification

Fresh slice-level verification was run after implementation via `gsd_exec` purpose `fresh S01 slice-level verification` and passed with exit code 0. Commands covered all S01 task/slice checks: required production env contract grep plus `dotnet build`; shell syntax checks for `scripts/deploy-backend.sh` and `scripts/deploy-config.sh`; deploy health-gate marker grep for systemd, loopback URL, external nginx URL, health, and 502 categories; `dotnet test --filter "ApiEndpointTests"` with 10 passed / 0 failed / 0 skipped; and `bash scripts/verify/s01-api-production-contract.sh`, which ended with `==> S01 verify passed`.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

T04 included a focused test-host/runtime stabilization fix beyond the original verifier file itself: `ApiEndpointTests` now remove mixed EF provider registrations before rebinding SQLite, set the host environment to `Testing`, and `Program.cs` uses `EnsureCreatedAsync` in Testing while retaining migration-based startup elsewhere. This was necessary to make the verifier's targeted endpoint suite pass and to retire a pre-existing Npgsql+SQLite provider conflict.

## Known Limitations

Live production health was not executed from this auto-mode environment because server credentials/secrets are intentionally unavailable. The deploy gates and URLs are defined and locally verified, but actual production env values, Postgres/container state, nginx runtime state, and current external response must be proven during a real deploy. Blazor still has not been updated to consume this boundary; that is S02. Full-stack smoke aggregation remains S05, and real Postgres integration coverage remains S07.

## Follow-ups

S02 should consume the health/failure taxonomy when implementing Blazor-side diagnostics. S05 should reuse the S01 health URLs and marker categories instead of inventing parallel checks. S07 should exercise the non-Testing migration/startup path against Postgres. S08 should document the finalized production deployment shape and local verifier workflow.

## Files Created/Modified

- `infra/happygymstats-api.service` — Declares the API environment file and production env contract by name.
- `scripts/deploy-backend.sh` — Adds API env prechecks, backend health gates, loopback/external health probes, 502 categorization, and surfaces cache readiness diagnostics.
- `scripts/deploy-config.sh` — Adds configurable backend health-gate URLs/timeouts and surfaces readiness endpoints.
- `docs/DEPLOYMENT.md` — Documents the API production runtime contract, health gate behavior, local S01 verifier, and launch-profile guidance.
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs` — Adds structured surfaces 404 assertions and stabilizes WebApplicationFactory database/provider setup.
- `src/HappyGymStats.Api/Program.cs` — Makes startup database initialization environment-aware for Testing versus non-testing environments.
- `scripts/verify/s01-api-production-contract.sh` — Adds the local deterministic verifier for S01 production API contract drift.
- `.gsd/PROJECT.md` — Refreshes project status to reflect M003/S01 completion and current deployment recovery state.
