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

## Environment / override knobs
See script headers and `--help` output for:
- SSH host/user/key/proxy
- remote roots
- service name
- sudo behavior

## Production target
- `torn.geromet.com` static frontend
- `/api/*` proxied to `127.0.0.1:5047`
