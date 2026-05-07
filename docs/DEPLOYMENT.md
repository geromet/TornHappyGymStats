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
