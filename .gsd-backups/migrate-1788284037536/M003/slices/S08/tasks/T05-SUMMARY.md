---
id: T05
parent: S08
milestone: M003
key_files:
  - scripts/verify/s08-docs-contract.sh
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T20:07:46.895Z
blocker_discovered: false
---

# T05: Added a deterministic S08 docs contract drift verifier that fails fast on stale route/runtime claims across README, docs, and API .http examples.

**Added a deterministic S08 docs contract drift verifier that fails fast on stale route/runtime claims across README, docs, and API .http examples.**

## What Happened

Implemented `scripts/verify/s08-docs-contract.sh` as an explicit contract checker following the existing taxonomy verifier pattern (`set -euo pipefail`, file existence checks, token presence checks, and stale-claim absence guards). The script validates current-state markers for the Blazor + API + AdminPanel + Postgres + Keycloak shape, required `/api/v1/torn/*` routes, service names, smoke command references, and milestone audit linkage (`.gsd/milestones/M003/M003-ROADMAP.md`). I hit and fixed two verifier correctness issues during execution: (1) grep misparsed `--no-launch-profile` as an option, resolved by using `grep -Fq --`; and (2) an over-broad stale token (`/api/v1/import`) matched valid admin routes, narrowed to true stale non-v1 route checks (`/api/import`).

## Verification

Ran the task verification command `bash scripts/verify/s08-docs-contract.sh` and confirmed full PASS output. Also re-ran via `gsd_exec` to capture explicit exit code and duration metadata; command exited 0 and printed final success sentinel `S08 docs contract drift checks passed.`

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s08-docs-contract.sh` | 0 | ✅ pass | 37ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `scripts/verify/s08-docs-contract.sh`
