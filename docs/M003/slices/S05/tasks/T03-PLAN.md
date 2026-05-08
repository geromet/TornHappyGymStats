---
estimated_steps: 8
estimated_files: 1
skills_used: []
---

# T03: Add HTTP route checks for API Blazor and AdminPanel

Why: The core user path requires API health, surfaces latest, and Blazor home all to work together.

Do:
1. Add loopback and external API health checks.
2. Add surfaces latest check that accepts 200 with valid JSON or 404 structured no-cache depending on readiness policy, but treats 502 as a hard failure.
3. Add Blazor home reachability check.
4. Add AdminPanel loopback/external health and protected unauthenticated denial checks from S04.
5. Include short safe response excerpts on failures.

Done when: one smoke run can prove or localize the frontend/backend/AdminPanel HTTP route state.

## Inputs

- `.gsd/milestones/M003/slices/S02/S02-SUMMARY.md`
- `.gsd/milestones/M003/slices/S04/S04-SUMMARY.md`
- `scripts/verify/s04-adminpanel-route.sh`

## Expected Output

- `scripts/verify/production-smoke.sh`

## Verification

bash -n scripts/verify/production-smoke.sh && rg -n "api/v1/torn/health|api/v1/torn/surfaces/latest|admin/health|admin/api/v1/import-runs|Bad Gateway|502|Blazor|curl" scripts/verify/production-smoke.sh

## Observability Impact

Signals added/changed: HTTP route phase in smoke report.
How a future agent inspects this: smoke output and endpoint statuses.
Failure state exposed: bad gateway, no cache, route missing, protected endpoint accidentally public.
