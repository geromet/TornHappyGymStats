---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T02: Add backend deploy health gates

Why: The backend deploy currently restarts the API and stops, so a broken service is only discovered by the Blazor UI as 502.

Do:
1. Add a reusable shell helper or inline backend deploy phase that checks `systemctl status`/`is-active` for `happygymstats-api` after restart.
2. Add a loopback HTTP health check for `http://127.0.0.1:5047/api/v1/torn/health` with timeout and clear failure category.
3. Add an external/nginx HTTP health check for `https://torn.geromet.com/api/v1/torn/health`, with 502 called out separately from other non-2xx responses.
4. Ensure commands are non-interactive and do not require printing secrets.
5. Keep checks configurable through `deploy-config.sh` so production host/URLs can be overridden.

Done when: `scripts/deploy-backend.sh` fails fast after restart if service, loopback API, or nginx API health is bad.

## Inputs

- `scripts/deploy-backend.sh`
- `scripts/deploy-config.sh`
- `infra/nginx-torn.conf`

## Expected Output

- `scripts/deploy-backend.sh`
- `scripts/deploy-config.sh`

## Verification

bash -n scripts/deploy-backend.sh && bash -n scripts/deploy-config.sh && rg -n "health|is-active|systemctl|127.0.0.1:5047|torn.geromet.com/api/v1/torn/health|502" scripts/deploy-backend.sh scripts/deploy-config.sh

## Observability Impact

Signals added/changed: deploy phase health lines for service status, loopback HTTP, external HTTP.
How a future agent inspects this: run backend deploy or helper with safe target variables.
Failure state exposed: inactive service, connection refused, non-2xx health, nginx bad gateway.
