---
id: T01
parent: S05
milestone: M002
key_files:
  - scripts/verify/s05-local-surfaces.sh
  - README.md
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-01T21:33:41.114Z
blocker_discovered: false
---

# T01: Added `scripts/verify/s05-local-surfaces.sh` and README guidance to enforce local surfaces artifact readiness checks before S05 frontend work.

**Added `scripts/verify/s05-local-surfaces.sh` and README guidance to enforce local surfaces artifact readiness checks before S05 frontend work.**

## What Happened

Implemented a new executable verifier at `scripts/verify/s05-local-surfaces.sh` that starts the API locally, enforces API-key preconditions, enqueues an import job, waits with timeout for `web/data/surfaces/meta.json` and `web/data/surfaces/latest.json`, and validates required JSON keys (`version`, `series.gymCloud`) with clear failure messages. The script includes explicit failure-mode handling for missing credentials, API startup/import failures, timeout while waiting for artifacts, and malformed/empty JSON envelopes. Updated `README.md` with an S05 pre-frontend verification section pointing operators to this command as the deterministic readiness gate.

## Verification

Ran the required verification command `bash scripts/verify/s05-local-surfaces.sh`. In this environment it failed fast with exit code 2 because `TORN_API_KEY`/`HAPPYGYMSTATS_TORN_API_KEY` is not set, which validates the planned negative-path behavior for malformed inputs (missing API key env).

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s05-local-surfaces.sh` | 2 | ❌ fail | 0ms |

## Deviations

None.

## Known Issues

Positive-path artifact generation could not be executed in this auto-mode run because required import API key environment variables are unavailable in the execution environment.

## Files Created/Modified

- `scripts/verify/s05-local-surfaces.sh`
- `README.md`
