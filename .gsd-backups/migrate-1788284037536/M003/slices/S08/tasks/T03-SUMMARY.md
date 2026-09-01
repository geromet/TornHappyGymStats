---
id: T03
parent: S08
milestone: M003
key_files:
  - docs/SETUP.md
  - docs/DEPLOYMENT.md
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T20:03:55.903Z
blocker_discovered: false
---

# T03: Rewrote setup and deployment docs to match the API+Blazor+AdminPanel+Postgres+Keycloak runtime/deploy contract with current smoke and setup commands.

**Rewrote setup and deployment docs to match the API+Blazor+AdminPanel+Postgres+Keycloak runtime/deploy contract with current smoke and setup commands.**

## What Happened

Updated `docs/SETUP.md` and `docs/DEPLOYMENT.md` from stale static-frontend/SQLite guidance to current operational contracts derived from active scripts. Setup docs now list required env var names (`HAPPYGYMSTATS_CONNECTION_STRING`, `ProvisionalToken__SigningKey`, `HAPPYGYMSTATS_SURFACES_CACHE_DIR`, etc.), local API run guidance, explicit `--no-launch-profile` warning when pinning `ASPNETCORE_URLS`, and verification entrypoints including local surfaces verification and production smoke checks. Deployment docs now define setup-vs-deploy split, current service names (`happygymstats-api`, `happygymstats-blazor`, `happygymstats-adminpanel`), route/health boundaries, required env/secret policy, root paths, adminpanel bootstrap flow (`setup-adminpanel-server`), sudo/systemd expectations, and canonical `production-smoke` command semantics. Because Blazor/AdminPanel source projects are not present in this checkout, docs explicitly treat them as operational peers and anchor behavior to deploy/smoke contracts instead of pretending local project paths exist.

## Verification

Ran the task contract grep verifier to confirm both docs include the required runtime/deploy markers and `--no-launch-profile` guard language.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|production-smoke|setup-adminpanel-server|happygymstats-api|happygymstats-blazor|happygymstats-adminpanel|--no-launch-profile" docs/SETUP.md docs/DEPLOYMENT.md` | 0 | ✅ pass | 5ms |

## Deviations

The task plan requested local run commands for API/Blazor/AdminPanel; only API source exists in this repo. I documented API local run directly and documented Blazor/AdminPanel as operational-peer service contracts with concrete verification and deployment commands sourced from `scripts/verify/production-smoke.sh` and deploy scripts.

## Known Issues

None.

## Files Created/Modified

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`
