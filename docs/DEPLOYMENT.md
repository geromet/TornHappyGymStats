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
