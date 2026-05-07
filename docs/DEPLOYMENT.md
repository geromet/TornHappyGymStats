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

## Frontend
- Uploads `web/` to timestamped release directory
- Flips `/var/www/torn-frontend/current`

## Environment / override knobs
See script headers and `--help` output for:
- SSH host/user/key/proxy
- remote roots
- service name
- sudo behavior

## Production target
- `torn.geromet.com` static frontend
- `/api/*` proxied to `127.0.0.1:5047`

## AdminPanel nginx route setup (S04)

Use the dedicated host route config source:
- `infra/nginx-adminpanel.conf`

Run local/static verification freely:
```bash
bash -n scripts/setup-adminpanel-server.sh
```

Remote setup is intentionally gated behind explicit user confirmation because it mutates privileged nginx paths.
Do not run remote setup unless you intentionally confirm both flags:
```bash
DEPLOY_INSTALL_ADMIN_NGINX=1 \
  bash scripts/setup-adminpanel-server.sh --execute --confirm-remote-setup
```

Safety behavior in the setup script:
- installs `nginx-adminpanel.conf` into `sites-available` + `sites-enabled` by default
- optional `conf.d` mode via `DEPLOY_ADMIN_NGINX_USE_CONF_D=1`
- runs `nginx -t` before attempting reload nginx
- runs `systemctl reload nginx` only after validation passes
- no route mutation occurs without explicit confirmation flags

## Production smoke verification (S05)

Canonical post-deploy smoke command:

```bash
bash scripts/verify/production-smoke.sh
```

Optional remote execution (same read-only checks, over SSH):

```bash
SMOKE_MODE=remote bash scripts/verify/production-smoke.sh
```

Optional local contract verifier for drift (syntax + required tokens + docs contract):

```bash
bash scripts/verify/s05-production-smoke-contract.sh
```

### Privilege and safety expectations
- Smoke script is read-only: no deploy/restart/mutation operations are performed.
- Service/port/nginx checks may need host privileges depending on machine policy.
- Remote mode requires SSH access configured via `SMOKE_SSH_*` variables.
- Script does not require Torn API key and does not print env file contents.

### Phase contract
The smoke script emits phase-organized output and a summary line:
- phases: `framework`, `services`, `http-routes`, `containers`, `summary`
- summary signal: `RESULT required_failures=<n> optional_warnings=<n>`

Exit behavior:
- any `required_failures > 0` => non-zero exit code
- `optional_warnings` do not fail the run (warning-only visibility)

### Expected failure categories
Required boundary failures include:
- missing/inactive systemd units (`missing`, `inactive-state=*`, `systemd-unavailable`, `no-privilege`)
- invalid nginx configuration (`nginx -t failed` / unavailable)
- missing required listener ports (`not-listening`)
- API/route boundary failures (including explicit 502 Bad Gateway on required surfaces/auth probes)

Optional visibility failures include:
- container lookup failures (`not-found`)
- docker inspection unavailability (`docker-access-unavailable`)
- container unhealthy state (`state!=running` or `health!=healthy/none`)
