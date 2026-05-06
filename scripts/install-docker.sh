#!/bin/bash
# Run once on the server: ssh -t ... 'bash -s' < scripts/install-docker.sh
set -euo pipefail

echo "=== Installing Docker Engine on Ubuntu 24.04 ==="

sudo apt-get update -q
sudo apt-get install -y ca-certificates curl

sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update -q
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

sudo usermod -aG docker "$USER"

echo ""
echo "Docker installed. Version:"
docker --version
echo ""
echo "NOTE: Log out and back in (or run 'newgrp docker') for group membership to take effect."
