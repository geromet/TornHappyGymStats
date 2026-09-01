---
id: T01
parent: S09
milestone: M003
key_files:
  - docs/SETUP.md
  - docs/DEPLOYMENT.md
  - global.json
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T20:11:23.976Z
blocker_discovered: false
---

# T01: Documented the .NET runtime contract in setup/deployment docs and added a pinned global.json SDK to make build/deploy expectations deterministic.

**Documented the .NET runtime contract in setup/deployment docs and added a pinned global.json SDK to make build/deploy expectations deterministic.**

## What Happened

Inspected the actual solution and project files first and found local reality differed from the slice rationale: all tracked projects in this checkout target net8.0, and deploy scripts publish API/AdminPanel as self-contained linux-x64 artifacts. Updated docs/SETUP.md with an explicit M003 S09 SDK/runtime contract section and docs/DEPLOYMENT.md with a deployment runtime/publish contract section that explains self-contained implications for server runtime requirements. Added root global.json to enforce an explicit SDK contract; initial pin (8.0.415) failed verification because it was not installed in this environment, so it was corrected to installed SDK 8.0.126 to preserve deterministic resolution and keep the verification gate passing.

## Verification

Ran the task verification command and confirmed the pinned SDK resolves plus required runtime tokens are present in the intended contract docs. Verification used dotnet --version and targeted ripgrep on docs/SETUP.md and docs/DEPLOYMENT.md for net10.0|SDK|runtime|linux-x64|self-contained markers.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet --version && rg -n "net10.0|SDK|runtime|linux-x64|self-contained" docs/SETUP.md docs/DEPLOYMENT.md` | 0 | ✅ pass | 176ms |

## Deviations

Minor factual correction from plan context: repository currently targets net8.0 (not net10.0) in all tracked csproj files in this checkout; documentation was aligned to observed source/deploy scripts without changing framework versions.

## Known Issues

None.

## Files Created/Modified

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`
- `global.json`
