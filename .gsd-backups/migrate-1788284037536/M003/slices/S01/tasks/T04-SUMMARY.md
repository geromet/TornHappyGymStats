---
id: T04
parent: S01
milestone: M003
key_files:
  - scripts/verify/s01-api-production-contract.sh
  - docs/DEPLOYMENT.md
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - src/HappyGymStats.Api/Program.cs
key_decisions:
  - Route contract checks in the S01 verifier assert deploy health URL variables in `deploy-backend.sh` and concrete route literals in `deploy-config.sh`, avoiding brittle assumptions about where route strings are declared.
  - Startup database initialization is environment-aware: `Testing` uses `EnsureCreatedAsync` for WebApplicationFactory SQLite runs, while non-testing environments retain migration-based startup (`MigrateAsync`).
duration: 
verification_result: mixed
completed_at: 2026-05-06T19:21:28.158Z
blocker_discovered: false
---

# T04: Added `scripts/verify/s01-api-production-contract.sh` as a deterministic local S01 contract gate, documented it, and fixed ApiEndpointTests host DB initialization so the verifier’s targeted endpoint suite passes.

**Added `scripts/verify/s01-api-production-contract.sh` as a deterministic local S01 contract gate, documented it, and fixed ApiEndpointTests host DB initialization so the verifier’s targeted endpoint suite passes.**

## What Happened

Implemented the new local verifier script at `scripts/verify/s01-api-production-contract.sh` to provide a single command for S01 API contract validation. The script performs file/syntax checks, static token checks for deploy precheck and health-gate categories, verifies route anchors via deploy config, enforces the `--no-launch-profile` gotcha when `ASPNETCORE_URLS` is pinned, runs targeted `ApiEndpointTests`, and keeps remote checks opt-in via `S01_ALLOW_REMOTE_URL_CHECKS=1` (shape checks only, no network calls by default). I also updated `docs/DEPLOYMENT.md` with a dedicated Local S01 contract verifier section and explicit `--no-launch-profile` guidance. During verification, `ApiEndpointTests` initially failed due to test host DB provider conflicts and startup migration behavior under SQLite; I fixed this by removing all existing EF context option registrations before rebinding SQLite in `ApiEndpointTests.TestApplicationFactory`, switching test environment to `Testing`, and making API startup use `EnsureCreatedAsync` in `Testing` while keeping `MigrateAsync` for non-testing environments.

## Verification

Ran the task-level verification command `bash scripts/verify/s01-api-production-contract.sh`. The final run passed all script phases: file/syntax checks, launch-profile guard, deploy token checks, targeted `ApiEndpointTests` (10/10 passed), and docs anchors. This confirms the local S01 verifier is functional and catches contract drift as intended.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s01-api-production-contract.sh` | 1 | ❌ fail | 28000ms |
| 2 | `bash scripts/verify/s01-api-production-contract.sh` | 1 | ❌ fail | 41000ms |
| 3 | `bash scripts/verify/s01-api-production-contract.sh` | 1 | ❌ fail | 49000ms |
| 4 | `bash scripts/verify/s01-api-production-contract.sh` | 0 | ✅ pass | 62000ms |

## Deviations

Included a focused test-host/runtime stabilization fix (`ApiEndpointTests` DI cleanup and `Program.cs` testing DB init branch) because the verifier’s required endpoint test suite was failing from pre-existing runtime/test wiring issues, preventing contract verification completion.

## Known Issues

`dotnet test` emits existing dependency vulnerability warnings (`NU1903` for `System.Security.Cryptography.Xml`) and existing EF1002 raw SQL warnings in unrelated tests; these are unchanged by this task.

## Files Created/Modified

- `scripts/verify/s01-api-production-contract.sh`
- `docs/DEPLOYMENT.md`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `src/HappyGymStats.Api/Program.cs`
