#!/usr/bin/env bash
# deploy-backend.sh — Publish API and deploy to server over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-backend.sh

Publishes HappyGymStats.Api and deploys it via SSH:
  - uploads to timestamped release dir
  - flips current symlink
  - enforces release/data permissions
  - optionally restarts systemd service
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_REMOTE_ROOT:=/var/www/happygymstats}"
: "${DEPLOY_REMOTE_SERVICE:=happygymstats-api}"
: "${DEPLOY_BACKEND_OWNER:=www-data}"
: "${DEPLOY_BACKEND_GROUP:=www-data}"
: "${DEPLOY_CONFIGURATION:=Release}"
: "${DEPLOY_RUNTIME:=linux-x64}"
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=0}"
: "${DEPLOY_RESTART_SERVICE:=1}"

readonly PUBLISH_DIR="${ROOT_DIR}/dist/backend-api"
readonly REMOTE_RELEASES_DIR="${DEPLOY_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/happygymstats-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_cmd_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_cmd_pipe() { ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

echo "==> Publishing API"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${API_PROJECT}" -c "${DEPLOY_CONFIGURATION}" -r "${DEPLOY_RUNTIME}" --self-contained true -o "${PUBLISH_DIR}"

if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
  [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]] && SUDO_CMD="sudo -n" || SUDO_CMD="sudo"
else
  SUDO_CMD=""
fi

echo "==> Uploading payload"
tar -C "${PUBLISH_DIR}" -cf - . | ssh_cmd_pipe "set -euo pipefail; mkdir -p '${REMOTE_STAGING_DIR}'; rm -rf '${REMOTE_STAGING_DIR}'/*; tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating backend release"
ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_REMOTE_ROOT}/data'; \
  ${SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi; \
  ${SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  ${SUDO_CMD} chown -R '${DEPLOY_BACKEND_OWNER}:${DEPLOY_BACKEND_GROUP}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_REMOTE_ROOT}/data'; \
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;; \
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;; \
  if [[ -f '${REMOTE_RELEASE_DIR}/HappyGymStats.Api' ]]; then ${SUDO_CMD} chmod 755 '${REMOTE_RELEASE_DIR}/HappyGymStats.Api'; fi; \
  rm -rf '${REMOTE_STAGING_DIR}'"

if [[ "${DEPLOY_RESTART_SERVICE}" == "1" ]]; then
  echo "==> Restarting service ${DEPLOY_REMOTE_SERVICE}"
  ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} systemctl restart '${DEPLOY_REMOTE_SERVICE}'; ${SUDO_CMD} systemctl --no-pager --full status '${DEPLOY_REMOTE_SERVICE}' | head -n 25"
else
  echo "==> Skipping service restart"
fi

echo "==> Backend deployment complete"