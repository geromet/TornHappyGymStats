#!/bin/bash
# Creates a dedicated 'happygym-containers' user for managing Docker containers.
# Run interactively: ssh -t ... 'bash -s' won't work — scp first, then ssh -t to run.
set -euo pipefail

USERNAME="happygym-containers"
DEPLOY_DIR="/opt/happygymstats/containers"

echo "=== Creating user: $USERNAME ==="
if id "$USERNAME" &>/dev/null; then
    echo "User $USERNAME already exists, skipping creation."
else
    sudo useradd --system --shell /bin/bash --create-home "$USERNAME"
fi
sudo usermod -aG docker "$USERNAME"

echo ""
echo "Set password for $USERNAME:"
sudo passwd "$USERNAME"

echo ""
echo "=== Creating deploy directory ==="
sudo mkdir -p "$DEPLOY_DIR"
sudo chown "$USERNAME:$USERNAME" "$DEPLOY_DIR"
sudo chmod 750 "$DEPLOY_DIR"

echo ""
echo "=== Creating .env ==="
echo "You will be prompted for each value. Input is hidden."
read_secret() {
    local prompt="$1"
    local val
    read -rsp "$prompt: " val
    echo
    echo "$val"
}

POSTGRES_PASSWORD=$(read_secret "POSTGRES_PASSWORD")
KEYCLOAK_DB_PASSWORD=$(read_secret "KEYCLOAK_DB_PASSWORD")
KEYCLOAK_ADMIN_PASSWORD=$(read_secret "KEYCLOAK_ADMIN_PASSWORD")

printf 'POSTGRES_PASSWORD=%s\nKEYCLOAK_DB_PASSWORD=%s\nKEYCLOAK_ADMIN_USER=admin\nKEYCLOAK_ADMIN_PASSWORD=%s\n' \
    "$POSTGRES_PASSWORD" "$KEYCLOAK_DB_PASSWORD" "$KEYCLOAK_ADMIN_PASSWORD" \
    | sudo tee "$DEPLOY_DIR/.env" > /dev/null

sudo chown "$USERNAME:$USERNAME" "$DEPLOY_DIR/.env"
sudo chmod 600 "$DEPLOY_DIR/.env"

echo ""
echo "Done."
echo "  User:       $USERNAME"
echo "  Deploy dir: $DEPLOY_DIR"
echo "  .env:       $DEPLOY_DIR/.env (chmod 600)"
