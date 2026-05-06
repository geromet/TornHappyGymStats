---
estimated_steps: 8
estimated_files: 3
skills_used: []
---

# T01: Decide AdminPanel nginx route ownership

Why: The AdminPanel service has a port but no external route. A host/path decision needs to happen before writing nginx config and smoke checks.

Do:
1. Review current nginx hosts and docs: `torn.geromet.com`, `auth.geromet.com`, and any DNS/cert constraints.
2. Choose and document the AdminPanel route: separate host such as `admin.geromet.com` or path under an existing host.
3. Prefer the route that minimizes collision with `/api/`, Blazor SignalR, and auth host responsibilities.
4. Record assumptions about Cloudflare/DNS/origin certificate coverage.
5. Add docs that distinguish public health from protected admin APIs.

Done when: the route decision is explicit and downstream config has a single target.

## Inputs

- `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`
- `infra/nginx-torn.conf`
- `infra/nginx-auth.conf`
- `docs/v2-plan.md`

## Expected Output

- `docs/DEPLOYMENT.md`

## Verification

rg -n "AdminPanel|admin\.geromet\.com|/admin|5048|auth-gated|health" docs/DEPLOYMENT.md

## Observability Impact

Signals added/changed: documented route ownership and auth boundary.
How a future agent inspects this: deployment docs and nginx file names.
Failure state exposed: route collision risk before implementation.
