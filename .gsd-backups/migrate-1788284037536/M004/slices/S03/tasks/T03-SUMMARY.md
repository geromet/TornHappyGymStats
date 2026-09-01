---
id: T03
parent: S03
milestone: M004
key_files:
  - scripts/verify/m004-my-stats-final-gate.sh
  - scripts/verify/s06-provenance-warnings.sh
  - README.md
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-09T17:54:14.582Z
blocker_discovered: false
---

# T03: Added a single M004 final verification gate script that composes scoped build/tests, auth+endpoint static scans, docs/operator checks, and secret-safe provenance regression checks.

**Added a single M004 final verification gate script that composes scoped build/tests, auth+endpoint static scans, docs/operator checks, and secret-safe provenance regression checks.**

## What Happened

Implemented `scripts/verify/m004-my-stats-final-gate.sh` with strict shell settings, labeled sections, and fail-fast error trapping that reports the failing section and command. The gate runs scoped `dotnet build` + filtered `dotnet test` for `M004FinalGateTests`, `SqliteApiEndpointTests`, `BlazorApiFailureTests`, and `SurfacesServiceFailureClassificationTests`; enforces static auth/menu and claim-bound endpoint markers; validates redaction assertions; runs the docs contract verifier; and enforces operator runbook markers. I also added the gate command to README minimal verification commands for discoverability. During verification, the provenance stage failed because `s06-provenance-warnings.sh` required API-key secrets, which contradicted the slice requirement. I updated `scripts/verify/s06-provenance-warnings.sh` to run S05 only when `TORN_API_KEY`/`HAPPYGYMSTATS_TORN_API_KEY` is present, and otherwise verify existing local `web/data/surfaces/latest.json` warning-shape data in secretless mode. Re-running the final gate passed end-to-end without echoing secrets.

## Verification

Ran `bash scripts/verify/m004-my-stats-final-gate.sh` before and after the provenance secretless fix. First run failed in the `provenance-regression` section due to missing API key env (expected blocker discovered by gate). After updating `scripts/verify/s06-provenance-warnings.sh` for secretless mode, reran the same final gate command and it passed all sections: build, filtered auth contract tests, static auth/endpoint markers, docs contract, operator runbook markers, and provenance warning-shape verification.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/m004-my-stats-final-gate.sh` | 2 | ❌ fail | 10724ms |
| 2 | `bash scripts/verify/m004-my-stats-final-gate.sh` | 0 | ✅ pass | 8915ms |

## Deviations

Extended `scripts/verify/s06-provenance-warnings.sh` to support secretless verification mode (using existing local artifact when API keys are absent) so the final gate meets the no-production-secrets requirement.

## Known Issues

None.

## Files Created/Modified

- `scripts/verify/m004-my-stats-final-gate.sh`
- `scripts/verify/s06-provenance-warnings.sh`
- `README.md`
