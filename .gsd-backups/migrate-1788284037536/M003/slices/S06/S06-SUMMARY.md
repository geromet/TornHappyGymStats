---
id: S06
parent: M003
milestone: M003
provides:
  - Shared deploy configuration and helper patterns for backend, frontend, AdminPanel, and container deploy flows.
  - Machine-checkable deploy preconditions and setup-vs-deploy boundaries.
  - Canonical production-smoke integration point for deploy orchestration.
  - Deterministic local verifier for S06 deployment script contract drift.
requires:
  - slice: S03
    provides: AdminPanel setup/service boundary and manual bootstrap expectations.
  - slice: S05
    provides: Canonical production smoke command and failure taxonomy.
affects:
  - S08
  - S09
key_files:
  - scripts/deploy-config.sh
  - scripts/deploy-containers.sh
  - scripts/deploy-backend.sh
  - scripts/deploy-frontend.sh
  - scripts/deploy-adminpanel.sh
  - scripts/setup-adminpanel-server.sh
  - scripts/deploy.sh
  - scripts/verify/production-smoke.sh
  - scripts/verify/s06-deploy-script-contract.sh
  - docs/DEPLOYMENT.md
  - .gsd/PROJECT.md
key_decisions:
  - Standardized deploy preconditions in shared config with `DEPLOY_PRECHECK_FAIL` markers before publish/upload/restart paths.
  - Made AdminPanel steady-state deploy detect missing setup/service during precheck and point to `scripts/setup-adminpanel-server.sh --help`.
  - Connected deploy orchestration to the canonical production smoke script through `DEPLOY_RUN_SMOKE` rather than duplicating smoke logic.
  - Standardized operational script categorization with machine-readable safety markers.
  - Encoded intentional SSH literal exceptions as an explicit verifier allowlist rather than allowing silent duplication.
patterns_established:
  - Shared source-only deploy configuration module (`scripts/deploy-config.sh`) for SSH/proxy/sudo defaults, preconditions, and smoke helpers.
  - Deploy scripts run explicit precondition phases before publish/upload/restart and emit machine-checkable failure markers.
  - One-time privileged setup is categorized separately from steady-state deploy.
  - Deploys preserve release directory + symlink activation while adding precheck and smoke visibility.
  - Local contract verifier enforces deployment script invariants without touching production.
observability_surfaces:
  - `DEPLOY_PRECHECK_FAIL`-style precondition output.
  - `SCRIPT_CATEGORY`, `SCRIPT_MUTATES_SERVER_STATE`, and `SCRIPT_AUTOMATION_SAFE_DEFAULT` markers.
  - `DEPLOY_RUN_SMOKE`/`DEPLOY_SMOKE_MODE` controls and production-smoke next-step output.
  - `scripts/verify/s06-deploy-script-contract.sh` PASS/FAIL contract output ending in `RESULT failures=0`.
drill_down_paths:
  - .gsd/milestones/M003/slices/S06/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S06/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S06/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S06/tasks/T04-SUMMARY.md
  - .gsd/milestones/M003/slices/S06/tasks/T05-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-07T19:39:59.930Z
blocker_discovered: false
---

# S06: Normalize deployment scripts

**Backend, frontend, AdminPanel, and container deploy flows now share deploy configuration, fail early with machine-checkable preconditions, keep manual bootstrap separate from steady-state deploy, and point to the canonical production smoke gate.**

## What Happened

S06 normalized the deployment script layer around a shared `scripts/deploy-config.sh` module. That module now centralizes non-secret SSH/proxy/sudo defaults, `.env.deploy` loading, shared SSH helper functions, deploy precondition helpers, and post-deploy smoke hooks. `scripts/deploy-containers.sh` was created/refactored to consume the shared config instead of duplicating hardcoded SSH construction, and it now reports required local/remote preconditions through help output without printing secret values.

The steady-state app deploy scripts now run explicit precondition phases before publishing or activating releases. Backend and frontend deploys preserve their existing release-directory plus symlink activation behavior while adding checks for required local project artifacts, remote commands/directories/services, write privilege, and service state. `scripts/deploy-adminpanel.sh` was added with the same release/symlink deployment pattern and an early missing-service failure path that points operators to the manual setup flow rather than failing later during restart. One-time setup remains separated in `scripts/setup-adminpanel-server.sh`, which is categorized as manual bootstrap rather than a normal deploy path.

The deploy orchestration now integrates with S05's production smoke surface instead of reimplementing stack verification. `scripts/deploy.sh` exposes `DEPLOY_RUN_SMOKE`, `DEPLOY_SMOKE_SCRIPT`, and `DEPLOY_SMOKE_MODE`; when enabled, smoke failure fails the overall deploy, and individual component deploys print a consistent post-deploy next step. Operational scripts now publish machine-readable safety markers (`SCRIPT_CATEGORY`, `SCRIPT_MUTATES_SERVER_STATE`, `SCRIPT_AUTOMATION_SAFE_DEFAULT`) so automation can classify deploy, diagnostic, and manual-bootstrap scripts without parsing human prose. The new `scripts/verify/s06-deploy-script-contract.sh` enforces these conventions locally without touching production.

Downstream readers should treat `scripts/deploy-config.sh` as the deployment configuration boundary and `scripts/verify/s06-deploy-script-contract.sh` as the local drift detector for this slice. S08 documentation should describe the normalized flow, including `.env.deploy` overrides, precheck markers, setup-vs-deploy boundaries, and optional smoke execution. S09 can build on the script contract verifier and production smoke command when documenting runtime/package reproducibility.

Operational Readiness:
- Health signal: deploy scripts either print the canonical production smoke next step or optionally run `scripts/verify/production-smoke.sh` through `DEPLOY_RUN_SMOKE=1`.
- Failure signal: precondition failures use machine-checkable markers such as `DEPLOY_PRECHECK_FAIL` plus category-specific details; the S06 contract verifier prints PASS/FAIL lines and `RESULT failures=0` on success.
- Recovery procedure: for missing AdminPanel service/setup, run the manual bootstrap flow exposed by `scripts/setup-adminpanel-server.sh --help`; for missing local artifacts, provide the expected project/compose file or override the documented deploy variables; for smoke failures, use the production-smoke failure taxonomy from S05.
- Monitoring gaps: this slice proves local shell contracts and safe read-only/dry-run behavior, not a live remote deployment or long-term monitoring integration.

## Verification

Fresh slice-level verification was run after the last file update via `gsd_exec` (`purpose=S06 final verification after PROJECT refresh`) and passed with exit code 0. The final verification executed `bash scripts/verify/s06-deploy-script-contract.sh`, syntax-checked deploy/setup/smoke scripts, confirmed deploy help exposes `DEPLOY_RUN_SMOKE`, `DEPLOY_SMOKE_MODE`, and `production-smoke`, and confirmed prohibited paste-back instructions are absent from operational scripts/docs. The contract verifier reported `RESULT failures=0` and the final command printed `S06_FINAL_VERIFICATION_RESULT=PASS`. Earlier in the same closure, the full task-level slice verification was also rerun, covering T01 through T05: container deploy syntax/help and legacy SSH literal guard, shared precondition marker grep, smoke-hook references/help output, manual/diagnostic category markers, and the S06 contract verifier.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

Several plan-referenced scripts/artifacts differed from the current checkout. `scripts/deploy-config.sh`, `scripts/deploy-containers.sh`, and `scripts/deploy-adminpanel.sh` were missing and were created to satisfy the slice contract. T04 plan targets `scripts/recon-server.sh` and `scripts/server-create-containers-user.sh` were absent, so categorization was applied to the current operational equivalents: `scripts/setup-adminpanel-server.sh` and `scripts/verify/production-smoke.sh`.

## Known Limitations

`scripts/deploy-containers.sh` defaults to `infra/docker-compose.yml`, which is absent in this checkout unless overridden by `DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE`. `scripts/deploy-adminpanel.sh` defaults to `src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj`, which is also absent unless provided or overridden. S06 verified local contracts and safe dry-run/read-only behavior; it did not perform remote mutation.

## Follow-ups

S08 should update docs/API examples to describe `.env.deploy`, shared deploy config, setup-vs-deploy boundaries, precondition markers, and optional smoke execution. S09 should consume the script contract verifier and smoke command when documenting runtime/SDK/package reproducibility. Future deploy scripts should source `scripts/deploy-config.sh` and update `scripts/verify/s06-deploy-script-contract.sh` when adding intentional exceptions.

## Files Created/Modified

- `scripts/deploy-config.sh` — Shared deploy config, SSH/sudo helpers, precheck helpers, and smoke hook controls.
- `scripts/deploy-containers.sh` — Container deploy flow using shared config and explicit preconditions.
- `scripts/deploy-backend.sh` — Backend deploy preconditions and shared smoke next-step output while preserving release/symlink behavior.
- `scripts/deploy-frontend.sh` — Frontend deploy preconditions and shared smoke next-step output while preserving release/symlink behavior.
- `scripts/deploy-adminpanel.sh` — New AdminPanel steady-state deploy flow with preconditions, release/symlink activation, and setup guidance.
- `scripts/setup-adminpanel-server.sh` — Manual bootstrap script categorized with machine-readable safety markers.
- `scripts/deploy.sh` — Deploy orchestrator wired to optional canonical production smoke execution.
- `scripts/verify/production-smoke.sh` — Read-only diagnostic script categorized with machine-readable safety markers.
- `scripts/verify/s06-deploy-script-contract.sh` — New deterministic local contract verifier for S06 deploy script invariants.
- `docs/DEPLOYMENT.md` — Deployment taxonomy and script safety marker contract documentation.
- `.gsd/PROJECT.md` — Project status refreshed to include S06 normalized deployment script state.
