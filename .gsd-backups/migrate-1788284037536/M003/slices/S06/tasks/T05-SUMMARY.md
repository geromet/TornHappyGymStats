---
id: T05
parent: S06
milestone: M003
key_files:
  - scripts/verify/s06-deploy-script-contract.sh
key_decisions:
  - Encode intentional SSH literal exceptions as an explicit allowlist in the verifier (setup-adminpanel-server.sh and production-smoke.sh) while failing any new duplicates outside allowlisted scripts.
duration: 
verification_result: passed
completed_at: 2026-05-07T19:38:18.750Z
blocker_discovered: false
---

# T05: Added `scripts/verify/s06-deploy-script-contract.sh` to enforce S06 deploy-script contracts (syntax, shared SSH literals, release activation tokens, AdminPanel setup hint, and smoke-hook wiring).

**Added `scripts/verify/s06-deploy-script-contract.sh` to enforce S06 deploy-script contracts (syntax, shared SSH literals, release activation tokens, AdminPanel setup hint, and smoke-hook wiring).**

## What Happened

Implemented a new deterministic local verifier at `scripts/verify/s06-deploy-script-contract.sh` in the same PASS/FAIL style as existing verify scripts. The verifier checks file presence/executability, runs `bash -n` over deploy/setup/smoke scripts, enforces that hardcoded SSH proxy/key literals are not duplicated outside shared config (with an explicit allowlist for setup/smoke scripts), asserts release/symlink activation tokens in backend/frontend/admin deploy scripts, confirms AdminPanel missing-service setup guidance token presence, and verifies smoke hook helper/reference wiring across deploy config and deploy scripts. Initial run failed due to intentional literals in setup/smoke scripts; updated the contract to encode this explicit allowlist per plan language ('unless explicitly allowed'), then reran verification to green.

## Verification

Ran the task verification command `bash scripts/verify/s06-deploy-script-contract.sh` and confirmed all contract checks pass with `RESULT failures=0`. Also ran a direct syntax check `bash -n scripts/deploy-config.sh scripts/setup-adminpanel-server.sh` to satisfy the previously failing verification gate with current script paths and syntax state.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s06-deploy-script-contract.sh` | 0 | ✅ pass | 72ms |
| 2 | `bash -n scripts/deploy-config.sh scripts/setup-adminpanel-server.sh` | 0 | ✅ pass | 2ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `scripts/verify/s06-deploy-script-contract.sh`
