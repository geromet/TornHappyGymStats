---
id: T04
parent: S08
milestone: M003
key_files:
  - src/HappyGymStats.Api/HappyGymStats.Api.http
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T20:05:45.753Z
blocker_discovered: false
---

# T04: Updated HappyGymStats.Api.http to the live /api/v1/torn route contract with safe import placeholders and current local host defaults.

**Updated HappyGymStats.Api.http to the live /api/v1/torn route contract with safe import placeholders and current local host defaults.**

## What Happened

I replaced stale `/v1/*` examples with the actual Minimal API routes defined in `src/HappyGymStats.Api/Program.cs`, including health, import latest/start, surfaces meta/latest, and read-model paging endpoints. I kept host default at `http://localhost:5047`, switched API key usage to an explicit placeholder variable, and added clear notes that surfaces endpoints may return 404 until cache exists. Because the task-plan input paths pointed to non-existent Controllers files, I adapted by validating route truth directly from Program.cs and aligned the `.http` file to that runtime contract. I also included a safe identity/faction note tied to cached surfaces output without inventing unsupported API endpoints.

## Verification

Ran the task verifier command to confirm required `/api/v1/torn/*` markers exist in the `.http` file and stale `/v1` route forms were removed.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `rg -n "/api/v1/torn/health|/api/v1/torn/surfaces/latest|/api/v1/torn/import-jobs" src/HappyGymStats.Api/HappyGymStats.Api.http && ! rg -n "GET .* /v1/|POST .* /v1/|localhost:5047/v1" src/HappyGymStats.Api/HappyGymStats.Api.http` | 0 | ✅ pass | 9ms |

## Deviations

Task-plan input file paths referenced `Controllers/*.cs`, but this codebase now exposes routes via Minimal APIs in `src/HappyGymStats.Api/Program.cs`; I verified and used that canonical source.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Api/HappyGymStats.Api.http`
