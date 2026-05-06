#!/bin/bash
# Deploys Keycloak + Postgres containers to the server.
# Requires Docker already installed (run install-docker.sh first).
# Usage: bash scripts/deploy-containers.sh
set -euo pipefail

SSH_KEY="$HOME/.ssh/id_token2_bio3_hetzner"
SSH_OPTS="-i $SSH_KEY -o 'ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com'"
SSH_HOST="anon@ssh.geromet.com"
REMOTE_DIR="/opt/happygymstats/containers"

echo "=== Deploying containers to $SSH_HOST ==="

# Copy infra files to server
echo "--- Copying infra files ---"
ssh -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    "$SSH_HOST" "mkdir -p $REMOTE_DIR/postgres-init"

scp -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    infra/docker-compose.yml \
    "$SSH_HOST:$REMOTE_DIR/"

scp -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    infra/postgres-init/01-keycloak-db.sh \
    "$SSH_HOST:$REMOTE_DIR/postgres-init/"

# Ensure init script is executable
ssh -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    "$SSH_HOST" "chmod +x $REMOTE_DIR/postgres-init/01-keycloak-db.sh"

# Check .env exists on server
echo "--- Checking .env on server ---"
ssh -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    "$SSH_HOST" "
    if [ ! -f $REMOTE_DIR/.env ]; then
        echo 'ERROR: $REMOTE_DIR/.env not found on server.'
        echo 'Copy infra/.env.example to $REMOTE_DIR/.env on the server and fill in the values.'
        exit 1
    fi
    echo '.env found.'
"

# Pull images and start containers
echo "--- Starting containers ---"
ssh -i "$SSH_KEY" -o "ProxyCommand=cloudflared access ssh --hostname ssh.geromet.com" \
    "$SSH_HOST" "cd $REMOTE_DIR && docker compose pull && docker compose up -d"

echo ""
echo "=== Done. Check status with: ==="
echo "ssh ... 'cd $REMOTE_DIR && docker compose ps'"
