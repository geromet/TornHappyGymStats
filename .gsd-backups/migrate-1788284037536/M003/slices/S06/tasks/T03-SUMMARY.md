---
id: T03
parent: S06
milestone: M003
key_files:
  - scripts/deploy-config.sh
  - scripts/deploy.sh
  - scripts/deploy-backend.sh
  - scripts/deploy-frontend.sh
  - scripts/deploy-adminpanel.sh
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T19:33:45.029Z
blocker_discovered: false
---

# T03: Connected deploy orchestration to a shared production-smoke hook with optional automatic smoke execution and fail-on-smoke semantics.

**Connected deploy orchestration to a shared production-smoke hook with optional automatic smoke execution and fail-on-smoke semantics.**

## What Happened

Implemented shared post-deploy smoke helpers in scripts/deploy-config.sh (`deploy_print_post_deploy_smoke_next_step` and `deploy_run_post_deploy_smoke_if_enabled`) with `DEPLOY_RUN_SMOKE`, `DEPLOY_SMOKE_SCRIPT`, and `DEPLOY_SMOKE_MODE` controls. Updated scripts/deploy.sh to source shared config, document smoke controls in --help output, run backend/frontend targets, and then execute the shared smoke hook exactly once per deploy invocation so smoke failure fails the overall deploy when enabled. Updated backend/frontend/adminpanel deploy scripts to emit consistent post-deploy smoke next-step signals via the shared helper and changed terminal wording to "release activation complete" so they do not imply full stack verification independently.

## Verification

Ran shell syntax and smoke-wiring discovery checks across deploy scripts, and confirmed deploy.sh help output exposes `DEPLOY_RUN_SMOKE`/`DEPLOY_SMOKE_MODE` and production smoke invocation guidance.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash -n scripts/deploy.sh scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "production-smoke|DEPLOY_RUN_SMOKE|smoke" scripts/deploy*.sh` | 0 | ✅ pass | 20ms |
| 2 | `bash scripts/deploy.sh --help | rg -n "DEPLOY_RUN_SMOKE|DEPLOY_SMOKE_MODE|production-smoke"` | 0 | ✅ pass | 17ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `scripts/deploy-config.sh`
- `scripts/deploy.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`
