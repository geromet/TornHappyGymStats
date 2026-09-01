---
id: T03
parent: S01
milestone: M003
key_files:
  - scripts/deploy-backend.sh
  - scripts/deploy-config.sh
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
key_decisions:
  - Kept health API payload unchanged and implemented surfaces readiness as deploy-side checks to avoid exposing filesystem cache paths.
  - Treated `/api/v1/torn/surfaces/latest` structured 404 `not_found` as a distinct non-fatal readiness warning (`surfaces_latest_missing`) rather than collapsing it into generic API failure.
duration: 
verification_result: mixed
completed_at: 2026-05-06T19:17:44.233Z
blocker_discovered: false
---

# T03: Added deploy-time surfaces cache readiness gates and API tests to distinguish missing latest.json from API-down failures.

**Added deploy-time surfaces cache readiness gates and API tests to distinguish missing latest.json from API-down failures.**

## What Happened

Implemented script-side diagnostics in `scripts/deploy-backend.sh` so post-restart health checks now probe loopback surfaces endpoints in addition to API health. Added explicit categories for `surfaces_meta_*` failures and a distinct `DEPLOY_HEALTH_WARN: category=surfaces_latest_missing` when `/api/v1/torn/surfaces/latest` returns structured `404 not_found`, preserving compatibility while separating cache-not-ready from API-unreachable cases. Added corresponding deploy config URLs in `scripts/deploy-config.sh`. Extended `ApiEndpointTests` with explicit assertions that `/api/v1/torn/surfaces/latest` and `/api/v1/torn/surfaces/meta` return structured 404 envelopes when cache artifacts are absent.

## Verification

Ran the task verification command set. `dotnet test --filter "ApiEndpointTests"` failed due a pre-existing integration test host provider-mix issue (Npgsql + Sqlite both registered in the test service provider), not introduced by this task’s deploy script changes. The grep verification command passed and confirmed new surfaces/latest/not_found/cache diagnostics are present across deploy script and tests.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test --filter "ApiEndpointTests"` | 1 | ❌ fail | 4631ms |
| 2 | `rg -n "surfaces|latest.json|not_found|cache" scripts/deploy-backend.sh src/HappyGymStats.Api tests/HappyGymStats.Tests/ApiEndpointTests.cs` | 0 | ✅ pass | 11ms |

## Deviations

None.

## Known Issues

`dotnet test --filter "ApiEndpointTests"` currently fails in repository baseline with `System.InvalidOperationException` about mixed EF providers (`Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.EntityFrameworkCore.Sqlite`) in `ApiEndpointTests` host startup; this blocks green test evidence for this task.

## Files Created/Modified

- `scripts/deploy-backend.sh`
- `scripts/deploy-config.sh`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
