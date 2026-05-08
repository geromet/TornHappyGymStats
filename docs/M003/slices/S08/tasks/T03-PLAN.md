---
estimated_steps: 7
estimated_files: 2
skills_used: []
---

# T03: Rewrite setup and deployment docs

Why: Setup and deployment docs are the operational contract for the failure class reported by the user.

Do:
1. Update `docs/SETUP.md` with local prerequisites, container/Postgres assumptions, env var names, local run commands for API/Blazor/AdminPanel, and local verification scripts.
2. Update `docs/DEPLOYMENT.md` with service names, roots, nginx routes, setup vs deploy split, required env files, sudoers/systemd/admin setup, and production smoke command.
3. Include warnings about `--no-launch-profile` when pinning `ASPNETCORE_URLS` in verification scripts.
4. Keep secret handling explicit: use env names/secure collection, never commit values.

Done when: an operator can follow setup/deploy docs without relying on stale static frontend/SQLite paths.

## Inputs

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`
- `.gsd/milestones/M003/slices/S01/S01-SUMMARY.md`
- `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`
- `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`

## Expected Output

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`

## Verification

rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|production-smoke|setup-adminpanel-server|happygymstats-api|happygymstats-blazor|happygymstats-adminpanel|--no-launch-profile" docs/SETUP.md docs/DEPLOYMENT.md

## Observability Impact

Signals added/changed: docs point to service/smoke diagnostics.
How a future agent inspects this: read setup/deployment docs.
Failure state exposed: wrong setup sequence should be caught by docs command/path verification.
