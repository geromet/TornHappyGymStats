---
id: T03
parent: S02
milestone: M003
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor
key_decisions:
  - Mapped ApiFailure categories to fixed actionable UI text instead of rendering SafeMessage directly in Home catch paths to reduce leakage risk while preserving operator guidance.
  - Logged structured failure metadata as Endpoint + integer StatusCode + Category for load/import failures to support downstream diagnostics.
duration: 
verification_result: passed
completed_at: 2026-05-06T19:34:12.923Z
blocker_discovered: false
---

# T03: Updated Blazor Home failure handling to render category-specific load/import diagnostics and structured endpoint/status/category logs without exposing API key data.

**Updated Blazor Home failure handling to render category-specific load/import diagnostics and structured endpoint/status/category logs without exposing API key data.**

## What Happened

Implemented T03 in `Home.razor` by replacing generic `ApiFailure.SafeMessage` rendering in both load and import catch paths with explicit category-based messages for API unavailable, reverse-proxy 502 bad gateway, validation/import rejection, and malformed API payloads. Preserved the existing no-data path for latest-surfaces 404 by keeping the `GetLatestAsync` null flow unchanged (`No surfaces data found. Run an import first.`). Updated structured logging calls to always include `Endpoint`, integer `StatusCode` (when present), and `Category` fields so operators can correlate UI failures with server logs while avoiding request secret leakage. The implementation intentionally does not include `_apiKey` in any message, scope, or exception output.

## Verification

Ran the task verification command end-to-end: `dotnet build && rg -n "Failed to load surfaces data|Bad Gateway|bad gateway|endpoint|status|ApiFailure" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`. Build succeeded and grep confirmed the new failure-state text and category-specific handling markers in Home.razor.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet build && rg -n "Failed to load surfaces data|Bad Gateway|bad gateway|endpoint|status|ApiFailure" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor` | 0 | ✅ pass | 5635ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`
