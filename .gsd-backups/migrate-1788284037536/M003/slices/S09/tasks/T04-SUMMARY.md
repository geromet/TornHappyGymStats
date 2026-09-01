---
id: T04
parent: S09
milestone: M003
key_files:
  - scripts/verify/s09-runtime-reproducibility.sh
key_decisions:
  - Reuse and delegate to `scripts/verify/s09-package-restore-policy.sh` instead of duplicating floating-version/lockfile logic so one policy source remains authoritative.
  - Verify runtime preflight via required marker tokens in `scripts/verify/production-smoke.sh` (`runtime-preflight` phase + runtime expectation variables) to keep this task local and deterministic.
duration: 
verification_result: passed
completed_at: 2026-05-08T00:33:31.390Z
blocker_discovered: false
---

# T04: Added `scripts/verify/s09-runtime-reproducibility.sh` to provide deterministic local proof of SDK/framework/package/runtime reproducibility before deploy.

**Added `scripts/verify/s09-runtime-reproducibility.sh` to provide deterministic local proof of SDK/framework/package/runtime reproducibility before deploy.**

## What Happened

Implemented a new S09 runtime reproducibility verifier script that runs locally with no production secrets or remote access requirements. The script enforces: (1) tool and SDK preflight against pinned `global.json`, (2) required docs and S09 contract sections, (3) `net8.0` target-framework compliance across tracked `.csproj`/`.fsproj`, (4) floating-version/lockfile policy by delegating to `scripts/verify/s09-package-restore-policy.sh`, (5) concrete `dotnet restore` and `dotnet build --no-restore`, and (6) runtime preflight token checks in `scripts/verify/production-smoke.sh`. It uses explicit PASS/FAIL/WARN signals and does not mask required failures with `|| true`; optional environment surfaces (e.g., missing Docker locally) are emitted as warnings. During implementation, I corrected a stale plan input path mismatch by using the real tracked project files present in this checkout.

## Verification

Ran `bash scripts/verify/s09-runtime-reproducibility.sh` and confirmed `RESULT required_failures=0 optional_warnings=1`. Required checks passed for SDK pin/match, docs contract markers, all project target frameworks (`net8.0`), package restore policy verifier, restore/build, and runtime preflight tokens. Optional warning for missing Docker was reported as designed without failing the required contract.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s09-runtime-reproducibility.sh` | 0 | ✅ pass | 4679ms |

## Deviations

Task input referenced `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`, which does not exist in this checkout. Adapted framework verification to all tracked project files under `src/` and `tests/` (`*.csproj`/`*.fsproj`) to preserve the intended contract verification.

## Known Issues

None.

## Files Created/Modified

- `scripts/verify/s09-runtime-reproducibility.sh`
