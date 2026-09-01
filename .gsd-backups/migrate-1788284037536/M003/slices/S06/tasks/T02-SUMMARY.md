---
id: T02
parent: S06
milestone: M003
key_files:
  - scripts/deploy-config.sh
  - scripts/deploy-backend.sh
  - scripts/deploy-frontend.sh
  - scripts/deploy-adminpanel.sh
key_decisions:
  - Standardized deploy preconditions in shared config with `DEPLOY_PRECHECK_FAIL` markers before any publish/upload path.
  - Made AdminPanel deploy detect missing service/setup in precheck and surface a setup-script hint (`scripts/setup-adminpanel-server.sh --help`) instead of restart-time generic systemctl failure.
duration: 
verification_result: passed
completed_at: 2026-05-07T19:31:21.487Z
blocker_discovered: false
---

# T02: Added shared deploy precondition helpers and applied them to backend/frontend plus a new AdminPanel deploy script with explicit setup-missing guidance.

**Added shared deploy precondition helpers and applied them to backend/frontend plus a new AdminPanel deploy script with explicit setup-missing guidance.**

## What Happened

Implemented a shared precheck framework in `scripts/deploy-config.sh` to centralize deploy-time preconditions and machine-checkable failure markers before publish/activation. Added reusable checks for required local files/directories/commands, remote command availability, remote service existence, remote write privilege (without broad writes), and optional non-interactive sudo readiness. Refactored `scripts/deploy-backend.sh` and `scripts/deploy-frontend.sh` to source shared config and run a dedicated precondition phase before upload/publish while preserving existing release directory + symlink activation semantics. The planned AdminPanel deploy script was missing in this checkout, so I created `scripts/deploy-adminpanel.sh` with the same release/symlink deployment pattern and prechecks; it now detects missing remote AdminPanel service setup up front and points operators to `bash scripts/setup-adminpanel-server.sh --help` instead of failing later with a generic restart error.

## Verification

Ran the task verification contract exactly: shell syntax validation for `deploy-config`, backend/frontend/adminpanel deploy scripts, then grep checks for precondition/setup/service-status markers across deploy scripts and shared config. The command passed and confirmed explicit precondition tokens plus setup-adminpanel-server guidance wiring.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash -n scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "precheck|precondition|required|setup-adminpanel-server|is-active|systemctl status" scripts/deploy-*.sh scripts/deploy-config.sh` | 0 | ✅ pass | 78ms |

## Deviations

Task input referenced `scripts/deploy-adminpanel.sh` as existing, but it did not exist locally; I created it as part of this task to satisfy the shared-precondition contract and keep downstream S06 tasks unblocked.

## Known Issues

`scripts/deploy-adminpanel.sh` defaults `DEPLOY_ADMIN_PROJECT` to `src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj`, which is currently absent in this checkout; deploy now fails early with `DEPLOY_PRECHECK_FAIL category=missing_adminpanel_project` until that project artifact is present or override is provided.

## Files Created/Modified

- `scripts/deploy-config.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`
