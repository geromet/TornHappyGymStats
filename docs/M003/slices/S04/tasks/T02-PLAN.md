---
estimated_steps: 9
estimated_files: 4
skills_used: []
---

# T02: Add AdminPanel nginx upstream config

Why: Once route ownership is chosen, nginx needs a concrete upstream config that preserves headers and does not break existing Blazor/API routes. The config verifier must fail on real config problems; it must not swallow nginx failures with `|| true`.

Do:
1. Add or update nginx config for the AdminPanel upstream at `127.0.0.1:5048`.
2. Preserve TLS/header patterns already used by Torn/Auth hosts.
3. Include SignalR/WebSocket headers only if AdminPanel needs them; avoid cargo-culting if it is API-only.
4. Ensure `/admin/health` routes to AdminPanel and protected `/admin/api/v1/...` routes also reach the app.
5. Avoid shadowing `location /api/` or Blazor `location /` on `torn.geromet.com` if using a path.
6. Add a dedicated local verifier script for the nginx config. If `nginx` is not installed locally, the script may skip live nginx syntax validation with a clear warning, but it must still fail on missing required tokens and must not use `|| true` to mask failures.

Done when: nginx config clearly proxies AdminPanel and `bash scripts/verify/s04-adminpanel-nginx-config.sh` fails on missing config/tokens or nginx syntax errors when nginx validation is available.

## Inputs

- `docs/DEPLOYMENT.md`
- `infra/nginx-torn.conf`
- `infra/nginx-auth.conf`
- `infra/happygymstats-adminpanel.service`

## Expected Output

- `infra/nginx-adminpanel.conf`
- `scripts/verify/s04-adminpanel-nginx-config.sh`

## Verification

bash scripts/verify/s04-adminpanel-nginx-config.sh

## Observability Impact

Signals added/changed: deterministic nginx config verifier for AdminPanel upstream.
How a future agent inspects this: run `bash scripts/verify/s04-adminpanel-nginx-config.sh` and read nginx config.
Failure state exposed: missing route tokens, route collision markers, bad nginx syntax when nginx is available.
