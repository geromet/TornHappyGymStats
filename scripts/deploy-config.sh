#!/usr/bin/env bash
# deploy-config.sh — Shared deployment configuration. Source this; do not execute directly.
# Override any variable by setting it in .env.deploy before sourcing this file.

readonly _DEPLOY_CONFIG_LOADED=1

# SSH connection
: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"

# Build
: "${DEPLOY_CONFIGURATION:=Release}"
: "${DEPLOY_RUNTIME:=linux-x64}"

# Sudo
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=1}"

# Backend (API)
: "${DEPLOY_API_REMOTE_ROOT:=/var/www/happygymstats}"
: "${DEPLOY_API_SERVICE:=happygymstats-api}"
: "${DEPLOY_API_OWNER:=www-data}"
: "${DEPLOY_API_GROUP:=www-data}"
: "${DEPLOY_API_RESTART:=1}"
: "${DEPLOY_API_HEALTH_GATES:=1}"
: "${DEPLOY_API_LOOPBACK_HEALTH_URL:=http://127.0.0.1:5047/api/v1/torn/health}"
: "${DEPLOY_API_EXTERNAL_HEALTH_URL:=https://torn.geromet.com/api/v1/torn/health}"
: "${DEPLOY_API_LOOPBACK_SURFACES_META_URL:=http://127.0.0.1:5047/api/v1/torn/surfaces/meta}"
: "${DEPLOY_API_LOOPBACK_SURFACES_LATEST_URL:=http://127.0.0.1:5047/api/v1/torn/surfaces/latest}"
: "${DEPLOY_API_HEALTH_TIMEOUT_SECONDS:=10}"
: "${DEPLOY_API_HEALTH_BODY_MAX_BYTES:=200}"

# Frontend (Blazor)
: "${DEPLOY_BLAZOR_REMOTE_ROOT:=/var/www/happygymstats-blazor}"
: "${DEPLOY_BLAZOR_SERVICE:=happygymstats-blazor}"
: "${DEPLOY_BLAZOR_OWNER:=www-data}"
: "${DEPLOY_BLAZOR_GROUP:=www-data}"
: "${DEPLOY_BLAZOR_RESTART:=1}"

# Admin Panel
: "${DEPLOY_ADMINPANEL_REMOTE_ROOT:=/var/www/happygymstats-adminpanel}"
: "${DEPLOY_ADMINPANEL_SERVICE:=happygymstats-adminpanel}"
: "${DEPLOY_ADMINPANEL_OWNER:=www-data}"
: "${DEPLOY_ADMINPANEL_GROUP:=www-data}"
: "${DEPLOY_ADMINPANEL_RESTART:=1}"

# Derived SSH helpers — available after sourcing
SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_cmd_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_cmd_pipe() { ssh -T  "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
  [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]] && SUDO_CMD="sudo -n" || SUDO_CMD="sudo"
else
  SUDO_CMD=""
fi
