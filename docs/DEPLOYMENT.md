# Deployment

This document is the operational contract for deploying and verifying the current runtime shape:

- `happygymstats-api` (loopback `127.0.0.1:5047`)
- `happygymstats-blazor` (loopback `127.0.0.1:5182`)
- `happygymstats-adminpanel` (loopback `127.0.0.1:5048`)
- nginx routes for `/api/*`, `/`, and `/admin/*`
- optional Postgres/Keycloak container health visibility via smoke checks

## .NET runtime/publish contract (M003 S09)

- Repository projects currently target `net8.0`; build hosts should use the SDK pinned by root `global.json`.
- Backend and AdminPanel deploy flows publish for `linux-x64` with `--self-contained true`.
- Operational implication: target servers do not require a separately installed shared .NET/ASP.NET runtime for these self-contained services, but do require systemd/nginx/service wiring validated by smoke checks.

## Setup vs deploy (important split)

### One-time setup / bootstrap

- `bash scripts/setup-adminpanel-server.sh --help`
- Installs/updates nginx admin route config (`infra/nginx-adminpanel.conf`) with explicit confirmation flags.
- Use this for server bootstrapping and route setup changes, not routine deploys.

### Routine deploy

- `bash scripts/deploy-backend.sh` (API)
- `bash scripts/deploy-frontend.sh` (legacy static `web/` path, if still used)
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
- AdminPanel root: `/var/www/happygymstats-adminpanel`
- Legacy static frontend root: `/var/www/torn-frontend`

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
