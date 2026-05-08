---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T04: Add AdminPanel route and auth smoke checks

Why: AdminPanel should expose anonymous health while protecting admin APIs. This boundary needs a smoke check before the full-stack script consumes it.

Do:
1. Add route smoke checks for loopback health, external health, and unauthenticated protected endpoint denial.
2. Check expected statuses: health 2xx, protected 401/403, upstream failures categorized separately.
3. If external host/DNS is not configured by default, support configurable URL variables and local-only mode.
4. Avoid printing auth tokens; use unauthenticated negative checks only.
5. Do not run remote/external checks that mutate server state; this task's smoke checks must be read-only.

Done when: one S04 verifier proves AdminPanel routing and auth boundary assumptions where environment allows.

## Inputs

- `infra/nginx-adminpanel.conf`
- `src/HappyGymStats.AdminPanel/Controllers/AdminHealthController.cs`
- `src/HappyGymStats.AdminPanel/Controllers/AdminImportRunsController.cs`

## Expected Output

- `scripts/verify/s04-adminpanel-route.sh`

## Verification

bash scripts/verify/s04-adminpanel-route.sh

## Observability Impact

Signals added/changed: route/auth smoke output.
How a future agent inspects this: run `bash scripts/verify/s04-adminpanel-route.sh`.
Failure state exposed: health unavailable, protected endpoint public, nginx bad gateway.
