---
id: T02
parent: S09
milestone: M003
key_files:
  - scripts/verify/production-smoke.sh
  - scripts/deploy-config.sh
  - scripts/deploy-backend.sh
  - scripts/deploy-adminpanel.sh
  - scripts/deploy-frontend.sh
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T20:14:30.874Z
blocker_discovered: false
---

# T02: Added runtime-aware production preflight checks plus deploy-time executable validation for backend/admin self-contained publishes.

**Added runtime-aware production preflight checks plus deploy-time executable validation for backend/admin self-contained publishes.**

## What Happened

Implemented a runtime-preflight phase in `scripts/verify/production-smoke.sh` that publishes the expected runtime contract (`linux-x64`, self-contained mode), reports host architecture, and conditionally inspects `dotnet --info` / `dotnet --list-runtimes` when `dotnet` is available. The check is actionable: it fails only when runtime-dependent mode is declared and `dotnet` is missing, but passes when self-contained mode is expected. Added a shared deploy helper in `scripts/deploy-config.sh` to assert an executable file exists and is executable. Updated backend and adminpanel deploy scripts to log runtime contract, `chmod 755` their published binaries locally, and verify executable presence before upload; adminpanel remote activation now also enforces executable permission on the deployed binary. Updated frontend deploy preconditions to explicitly declare static-asset runtime expectations for observability consistency.

## Verification

Ran the task verification command exactly as planned: shell syntax validation across all touched scripts plus a grep check for runtime/executable markers. The command passed and output confirmed presence of runtime preflight signals (`dotnet --info`, `dotnet --list-runtimes`, runtime contract fields) and executable-permission enforcement (`chmod 755`, executable checks) in smoke/deploy scripts.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash -n scripts/verify/production-smoke.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "dotnet --info|list-runtimes|linux-x64|chmod 755|executable|runtime" scripts/verify/production-smoke.sh scripts/deploy-*.sh` | 0 | ✅ pass | 8ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `scripts/verify/production-smoke.sh`
- `scripts/deploy-config.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-adminpanel.sh`
- `scripts/deploy-frontend.sh`
