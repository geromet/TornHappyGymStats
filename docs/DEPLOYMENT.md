# Deployment

## Main scripts

Deploy all:

```bash
bash scripts/deploy.sh --target all
```

Deploy separately:

```bash
bash scripts/deploy-backend.sh
bash scripts/deploy-frontend.sh
```

## Backend
- Publishes `src/HappyGymStats.Api`
- Uploads to timestamped release directory
- Flips `current` symlink
- Restarts systemd service
- Runs a remote precheck for required API runtime env contract names before publish/restart

## API production runtime contract (required)

Systemd unit: `infra/happygymstats-api.service`

Server-local env file (never committed): `/etc/happygymstats/api.env`

Required keys (names only):
- `ConnectionStrings__HappyGymStats` **or** `HAPPYGYMSTATS_CONNECTION_STRING`
- `ProvisionalToken__SigningKey`
- `HAPPYGYMSTATS_SURFACES_CACHE_DIR`
- `ASPNETCORE_ENVIRONMENT`
- `ASPNETCORE_URLS`

### Contract alignment note

If nginx serves static surfaces from `/data/surfaces/` aliasing
`/var/www/happygymstats/data/surfaces-cache/`, set:

```dotenv
HAPPYGYMSTATS_SURFACES_CACHE_DIR=/var/www/happygymstats/data/surfaces-cache
```

This keeps API-generated surfaces and nginx static path aligned.

### Missing-contract failure signals

`bash scripts/deploy-backend.sh` fails fast with grep-able errors:
- `DEPLOY_PRECHECK_FAIL: missing_env_file path=/etc/happygymstats/api.env`
- `DEPLOY_PRECHECK_FAIL: missing_env_var ConnectionStrings__HappyGymStats_or_HAPPYGYMSTATS_CONNECTION_STRING`
- `DEPLOY_PRECHECK_FAIL: missing_env_var <KEY>`

No secret values are printed.

## Frontend
- Uploads `web/` to timestamped release directory
- Flips `/var/www/torn-frontend/current`

## Blazor server runtime API boundary

Systemd unit: `infra/happygymstats-blazor.service`

Server-side Blazor `HttpClient` calls execute on the Blazor host process, not in the browser. Because of that, production should call the API over loopback directly instead of routing through public Cloudflare/nginx.

Required production key (name/value contract):
- `ApiBaseUrl=http://127.0.0.1:5047`

Development contract:
- Keep `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.Development.json` set to a local developer API URL (currently `https://localhost:7047`).
- `Program.cs` now requires `ApiBaseUrl` (no `https://localhost:7001` fallback), so config drift fails fast and is visible.

## Local S01 contract verifier

Run this before production deploy/debug to prove S01 API contract drift locally:

```bash
bash scripts/verify/s01-api-production-contract.sh
```

The verifier is local-only by default (no remote network calls). It statically checks deploy precheck/health gate markers, runs `ApiEndpointTests`, and validates the launch-profile gotcha: when pinning `ASPNETCORE_URLS` in `dotnet run` verification scripts, include `--no-launch-profile` so launch settings do not override the URL.

## Environment / override knobs
See script headers and `--help` output for:
- SSH host/user/key/proxy
- remote roots
- service name
- sudo behavior

## AdminPanel sudoers + setup boundary (S03)

Source of truth: `infra/sudoers-happygymstats`

### Permanent (steady-state deploy)
These permissions are expected to remain after bootstrap and support normal deploys:
- release activation/file ownership operations (`rsync`, `mkdir`, `rm`, `ln`, `chown`, `chmod`, `find`)
- service `restart` + `status` for:
  - `happygymstats-api`
  - `happygymstats-blazor`
  - `happygymstats-adminpanel`

### Bootstrap-only (one-time setup)
These permissions exist to install and activate AdminPanel prerequisites:
- `install` for writing service/sudoers artifacts with controlled mode/ownership
- `visudo -cf /etc/sudoers.d/happygymstats` for sudoers syntax validation before activation
- `systemctl daemon-reload`
- `systemctl enable happygymstats-adminpanel`
- `systemctl start happygymstats-adminpanel`
- `systemctl status happygymstats-adminpanel` (also used in steady-state)

### Explicit hard boundaries
- No `NOPASSWD: ALL`
- No shell escalation commands (`/bin/bash`, `/usr/bin/bash`, `sh -c`)
- No wildcard service names; only `happygymstats-*` units explicitly listed above

### Setup execution + health proof
Run local static verification before attempting remote bootstrap:

```bash
bash scripts/verify/s03-adminpanel-setup.sh
```

Remote bootstrap command (mutates remote server state):

```bash
bash scripts/setup-adminpanel-server.sh
```

What mutates remotely:
- `/etc/sudoers.d/happygymstats` install/refresh (after `visudo -cf`)
- `/etc/systemd/system/happygymstats-adminpanel.service` install/refresh
- `systemctl daemon-reload`, `enable`, and `start`/`restart` for `happygymstats-adminpanel`

Agent safety requirement: obtain explicit user confirmation before running the non-dry-run setup command because it changes privileged server state.

## AdminPanel nginx route ownership (S04)

Decision: use a dedicated host, `admin.geromet.com`, for AdminPanel traffic, instead of mounting AdminPanel under `torn.geromet.com/admin/*` or `auth.geromet.com/admin/*`.

Why this host ownership is preferred:
- Avoids path collision with existing `torn.geromet.com` responsibilities (`/api/*`, static/frontend root, and Blazor SignalR/WebSocket behavior).
- Keeps `auth.geromet.com` focused on Keycloak/OIDC concerns and avoids mixing identity-host responsibilities with AdminPanel app routing.
- Gives nginx a single-purpose upstream boundary for AdminPanel (`127.0.0.1:5048`) with cleaner smoke checks and failure categorization.

Route/auth boundary contract:
- Public health endpoint: `https://admin.geromet.com/admin/health` should be reachable anonymously (2xx).
- Protected AdminPanel APIs (for example `/admin/api/v1/...`) must remain auth-gated and reject unauthenticated requests (401/403 expected).
- Do not log or print auth tokens in smoke checks; prefer unauthenticated negative assertions for protected routes.

Infra assumptions to validate during nginx implementation:
- DNS: Cloudflare record for `admin.geromet.com` points to the same VPS/origin as other hosts.
- TLS: existing Cloudflare origin certificate (`/etc/ssl/cloudflare/origin.pem`) covers `admin.geromet.com` (wildcard `*.geromet.com` or equivalent SAN).
- Origin reachability: AdminPanel systemd service remains loopback-bound at `127.0.0.1:5048`; nginx is the only external ingress path.

## Production targets
- `torn.geromet.com` static frontend + Blazor root
- `torn.geromet.com/api/*` proxied to `127.0.0.1:5047`
- `auth.geromet.com` Keycloak host
- `admin.geromet.com/admin/*` proxied to `127.0.0.1:5048` (with anonymous health and auth-gated admin APIs)
