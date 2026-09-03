# Deployment

This document is the operational contract for deploying and verifying the current runtime shape:

- `happygymstats-api` (loopback `127.0.0.1:5047`)
- `happygymstats-blazor` (loopback `127.0.0.1:5182`)
- `happygymstats-adminpanel` (loopback `127.0.0.1:5048`)
- nginx routes for `/api/*`, `/`, and `/admin/*`
- optional Postgres/Keycloak container health visibility via smoke checks

## .NET runtime/publish contract (M003 S09)

- Repository projects currently target `net10.0`; build hosts should use the SDK pinned by root `global.json`.
- Backend and AdminPanel deploy flows publish for `linux-x64` with `--self-contained true`.
- Operational implication: target servers do not require a separately installed shared .NET/ASP.NET runtime for these self-contained services, but do require systemd/nginx/service wiring validated by smoke checks.

## Setup vs deploy (important split)

### One-time setup / bootstrap

- `bash scripts/setup-adminpanel-server.sh --help`
- Installs/updates nginx admin route config (`infra/nginx-adminpanel.conf`) with explicit confirmation flags.
- Use this for server bootstrapping and route setup changes, not routine deploys.

### Routine deploy

- `bash scripts/deploy-backend.sh` (API)
- `bash scripts/deploy-frontend.sh` (Blazor frontend)
- `bash scripts/deploy-adminpanel.sh` (AdminPanel)
- `bash scripts/deploy.sh --target all` (orchestrated entrypoint)

### Post-deploy verification

- `bash scripts/verify/production-smoke.sh`
- Remote: `SMOKE_MODE=remote bash scripts/verify/production-smoke.sh`

## Required env files and secret policy

- Deployment scripts source `scripts/deploy-config.sh`, which can read `.env.deploy` when present.
- Production runtime env files (for systemd services) must be managed on host, outside git.
- Never commit secret values. Reference env var names only.

Critical API env var names:

- `HAPPYGYMSTATS_CONNECTION_STRING`
- `ConnectionStrings__HappyGymStats`
- `ProvisionalToken__SigningKey`
- `HAPPYGYMSTATS_SURFACES_CACHE_DIR`
- `ASPNETCORE_URLS`
- `ASPNETCORE_ENVIRONMENT`

## Service and release roots

Current deploy scripts enforce timestamped release + `current` symlink activation:

- API root: `/var/www/happygymstats`
- Blazor root: `/var/www/happygymstats-blazor`
- AdminPanel root: `/var/www/happygymstats-adminpanel`

Core units expected by smoke and deploy guards:

- `happygymstats-api`
- `happygymstats-blazor`
- `happygymstats-adminpanel`

## nginx routes and boundary checks

The runtime route contract checked by smoke:

- `https://torn.geromet.com/api/v1/torn/health` → API loopback
- `https://torn.geromet.com/api/v1/torn/surfaces/latest` → API surfaces endpoint (200 or structured 404)
- `https://torn.geromet.com/` → Blazor home
- `https://admin.geromet.com/admin/health` → AdminPanel health
- `https://admin.geromet.com/admin/api/v1/import-runs` should return auth denial (401/403) when unauthenticated

## Dev host — torndev.geromet.com

A second, private deployment of the same build for testing before production.

**Why a subdomain and not `torn.geromet.com/dev`.** The Blazor app hardcodes
`<base href="/" />` and runs Interactive Server, so a path mount would need a
different build (different base href, relocated `/_blazor` circuit, rewritten API
base) — meaning you would no longer be testing what you ship. It would also share
an origin, and therefore a cookie jar, with production: signing into dev could
clobber a live session. A dedicated host avoids all of it. This mirrors the
choice already made for `admin.geromet.com`.

`torndev` is deliberately a single-label subdomain: the Cloudflare origin
certificate covers `*.geromet.com` but not `*.*.geromet.com`, so
`dev.torn.geromet.com` would need a paid tier.

### Runtime shape

| | Production | Dev |
|---|---|---|
| Host | `torn.geromet.com` | `torndev.geromet.com` |
| API | `127.0.0.1:5047` | `127.0.0.1:5147` |
| Blazor | `127.0.0.1:5182` | `127.0.0.1:5282` |
| API root | `/var/www/happygymstats` | `/var/www/happygymstats-dev` |
| Blazor root | `/var/www/happygymstats-blazor` | `/var/www/happygymstats-blazor-dev` |
| Units | `happygymstats-api`, `happygymstats-blazor` | `happygymstats-api-dev`, `happygymstats-blazor-dev` |
| API env file | `/etc/happygymstats/api.env` | `/etc/happygymstats/api-dev.env` |
| Keycloak client | `happygymstats-web` | `happygymstats-web-dev` |
| Access | public | administrators only |

The published build is identical in both. Everything that differs lives in the
systemd units and the host env file.

### Operator prerequisites

These cannot be scripted from this repo:

1. **DNS** — Cloudflare A record `torndev.geromet.com` to the origin IP.
2. **Keycloak** — two clients in realm `torn`:
   - `happygymstats-web-dev`: redirect URI
     `https://torndev.geromet.com/signin-oidc`, post-logout redirect
     `https://torndev.geromet.com/signout-callback-oidc`, web origin
     `https://torndev.geromet.com`, no wildcards. A separate client, rather than
     a second redirect URI on `happygymstats-web`, is what stops a production
     session from being replayed against dev.
   - `happygymstats-api-dev`: a bearer-only stand-in that exists so an audience
     has a name. On `happygymstats-web-dev`'s dedicated scope, add an **Audience**
     mapper with Included Client Audience = `happygymstats-api-dev`, matching
     `Keycloak__Audience` in `happygymstats-api-dev.service`. Without it the two
     APIs accept the same tokens and the client separation buys nothing at the
     API layer.

   Your account must be in the `/admins` group, and the group must reach the
   token: the realm's `groups` client scope carries a Group Membership mapper
   (full path on, "Add to access token" and "Add to ID token" on) that emits the
   `groups` claim `RestrictedAccessExtensions.IsAdministrator` reads.
3. **Postgres** — a database and role for dev, separate from production. The API
   runs migrations at startup, so a shared database would let a dev build alter
   the production schema.
4. **Env file** — fill in `/etc/happygymstats/api-dev.env` after the setup script
   seeds it. It ships `REPLACE_ME` placeholders so the service fails loudly
   rather than starting against something real. `ProvisionalToken__SigningKey`
   must differ from production, or dev-minted tokens are valid there.

### Bootstrap and deploy

```bash
# 1) Static contract check — offline, no host needed
bash scripts/verify/devhost-contract.sh

# 2) Dry run: local checks only, prints what it would do
bash scripts/setup-devhost-server.sh

# 3) Bootstrap the host (nginx block, both units, release roots, env skeleton)
DEPLOY_INSTALL_DEV_HOST=1 \
  bash scripts/setup-devhost-server.sh --execute --confirm-remote-setup

# 4) Deploy code
bash scripts/deploy-dev.sh                      # or --target backend|frontend

# 5) Verify
bash scripts/verify/devhost-smoke.sh
```

`deploy-dev.sh` reuses `deploy-backend.sh` / `deploy-frontend.sh` with the dev
roots and units supplied through their existing env-var overrides, so the two
environments cannot drift apart. It refuses to run if pointed at a production
root or unit.

### Admin-only gate

`Access:RestrictToAdmins` (set as `Access__RestrictToAdmins=true` in the dev
Blazor unit) enables `RestrictedAccessExtensions`, which denies every request
that is not from an administrator. Unset — as in production — the middleware is
never registered.

It is middleware rather than an `AuthorizationOptions.FallbackPolicy` on purpose:
a fallback policy also captures the OIDC callback, static assets and the cookie
handler's `AccessDeniedPath`, so a signed-in non-admin would loop between the
challenge and the denial. The allowlist in `AlwaysAllowedPathPrefixes` keeps the
sign-in round trip reachable; anonymous visitors get a challenge, signed-in
non-admins get a flat 403.

Admin is recognised from the `admin` role claim, a flat `roles` claim, or the raw
Keycloak `/admins` group. All three are needed because the Blazor host registers
no `IClaimsTransformation` — unlike the API and AdminPanel, it never maps
`/admins` onto a role.

## AdminPanel setup details

Dry-run validation (safe):

```bash
bash -n scripts/setup-adminpanel-server.sh
```

Mutating setup requires explicit confirmation flags:

```bash
DEPLOY_INSTALL_ADMIN_NGINX=1 \
  bash scripts/setup-adminpanel-server.sh --execute --confirm-remote-setup
```

The setup script validates nginx config (`nginx -t`) before reload and does not mutate routes without explicit confirmation.

## Sudo/systemd/admin expectations

- Deploy scripts assume SSH access and (by default) sudo-enabled host operations.
- `deploy-config.sh` controls sudo behavior (`DEPLOY_USE_SUDO`, `DEPLOY_SUDO_NON_INTERACTIVE`).
- Service restarts and status checks use `systemctl`; missing privileges or missing units are hard failures in required checks.

## Production smoke verification (S05)

## Production smoke command (canonical)

```bash
bash scripts/verify/production-smoke.sh
```

This command is read-only (`SCRIPT_MUTATES_SERVER_STATE=0`) and emits phase-based diagnostics:

- framework
- services
- http-routes
- containers
- summary

Result contract:

- `RESULT required_failures=<n> optional_warnings=<n>`
- non-zero exit when `required_failures > 0`
- expected failure categories include `systemd-unavailable` and `docker-access-unavailable` when host capabilities or privileges are missing in the current execution context.

### Known operator caveat: nginx check permission in remote smoke

In some environments, remote smoke runs execute as a non-root SSH user. In that case the `nginx -t` check inside `scripts/verify/production-smoke.sh` can report a required failure caused by privilege denial (`Permission denied` on nginx config include files), even when nginx is actually healthy and routing correctly.

When all service, port, and HTTP route checks pass but nginx check fails with privilege-denied output, validate nginx config directly on host:

```bash
sudo nginx -t
```

Treat the deployment as healthy only if this privileged nginx test passes.

### Postgres credential mismatch recovery (API crash loop)

Symptom pattern:

- `happygymstats-api` repeatedly restarts with core-dump/ABRT
- `journalctl -u happygymstats-api` shows:
  - `Npgsql.PostgresException ... SqlState: 28P01`
  - `password authentication failed for user ...`

Recovery sequence:

1. Confirm API connection-string username/password source in `/etc/happygymstats/api.env` (`ConnectionStrings__HappyGymStats=...`).
2. Align the Postgres role password for the exact username used in that connection string.
3. Restart API and verify loopback health.

Containerized Postgres example (adapt username/container as needed):

```bash
sudo docker exec -i containers-postgres-1 bash -lc \
  'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-postgres}" -v ON_ERROR_STOP=1' <<'SQL'
ALTER USER <api-db-username> WITH PASSWORD '<same-password-as-api-env>';
SQL

sudo systemctl restart happygymstats-api
curl -sv http://127.0.0.1:5047/api/v1/torn/health
```

If loopback health is 200 after restart, downstream 502 errors on `torn.geromet.com` API routes should clear.

## `ASPNETCORE_URLS` verification warning

When running local verification flows that pin `ASPNETCORE_URLS`, always include `--no-launch-profile` for `dotnet run` so launch profile settings do not override your explicit URL binding. See `scripts/verify/s05-local-surfaces.sh` for the canonical pattern.

## Quick operator sequence

```bash
# 1) Deploy target (example: API)
bash scripts/deploy-backend.sh

# 2) Run smoke checks
bash scripts/verify/production-smoke.sh

# 3) If needed, run route/setup contract verifiers
bash scripts/verify/s05-production-smoke-contract.sh
bash scripts/verify/s06-deploy-script-contract.sh
```

If smoke fails, use emitted failure category and service/route phase to diagnose before rerunning deploy.
running deploy.
