---
id: T01
parent: S01
milestone: M003
key_files:
  - infra/happygymstats-api.service
  - scripts/deploy-backend.sh
  - docs/DEPLOYMENT.md
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-06T19:09:23.908Z
blocker_discovered: false
---

# T01: Declared and enforced the API production env contract across systemd, deploy precheck, and deployment docs with grep-able fail-fast signals.

**Declared and enforced the API production env contract across systemd, deploy precheck, and deployment docs with grep-able fail-fast signals.**

## What Happened

I updated `infra/happygymstats-api.service` to declare the server-local env contract source (`/etc/happygymstats/api.env`) and list required variable names without embedding any secret values. I then added a precheck phase in `scripts/deploy-backend.sh` that verifies the env file exists and required keys are present before publish/restart, emitting explicit `DEPLOY_PRECHECK_FAIL` markers for missing contract elements. Finally, I expanded `docs/DEPLOYMENT.md` with a dedicated API runtime contract section, including required key names, where they are loaded from, cache-path alignment guidance for nginx `/data/surfaces/`, and the exact failure markers operators can grep.

## Verification

Ran the task verification command from the plan: ripgrep over service/deploy/docs for required env contract names and `dotnet build` for repository build integrity. Verification passed with exit code 0; build completed successfully with warnings only.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ConnectionStrings__HappyGymStats|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|ASPNETCORE_URLS" infra/happygymstats-api.service scripts/deploy-backend.sh docs/DEPLOYMENT.md && dotnet build` | 0 | ✅ pass | 9648ms |

## Deviations

None.

## Known Issues

`dotnet build` reports pre-existing warnings (NU1903 vulnerability advisories and existing analyzer warnings) outside this task’s scope.

## Files Created/Modified

- `infra/happygymstats-api.service`
- `scripts/deploy-backend.sh`
- `docs/DEPLOYMENT.md`
