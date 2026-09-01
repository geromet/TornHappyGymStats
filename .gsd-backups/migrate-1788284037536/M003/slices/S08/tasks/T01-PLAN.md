---
estimated_steps: 7
estimated_files: 1
skills_used: []
---

# T01: Update README current-state summary

Why: README is the first context future agents read; it currently points to stale SQLite/static dashboard assumptions.

Do:
1. Update project summary to current Blazor + API + Postgres + Keycloak + AdminPanel shape.
2. Preserve legacy JSONL/SQLite/static web references only if explicitly labeled historical/legacy and still true.
3. Add quick links to setup, deployment, audit report, and smoke verification.
4. Keep commands minimal but current.

Done when: README no longer misidentifies the primary frontend or storage/deploy shape.

## Inputs

- `README.md`
- `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md`
- `.gsd/milestones/M003/M003-ROADMAP.md`

## Expected Output

- `README.md`

## Verification

rg -n "Blazor|Postgres|Keycloak|AdminPanel|production-smoke|2026-05-06-181943" README.md && ! rg -n "stores the result in SQLite|static dashboard" README.md

## Observability Impact

Signals added/changed: README points to current verification commands.
How a future agent inspects this: read README quick links.
Failure state exposed: stale architecture claims caught by docs verifier later.
