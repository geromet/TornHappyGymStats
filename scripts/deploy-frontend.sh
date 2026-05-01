#!/usr/bin/env bash
# deploy-frontend.sh — Deploy web/ to torn.geromet.com host over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly WEB_DIR="${ROOT_DIR}/web"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-frontend.sh

Deploys local web/ assets to the remote host:
  - uploads to timestamped release dir
  - flips current symlink
  - enforces frontend permissions
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
: "${DEPLOY_FRONTEND_REMOTE_ROOT:=/var/www/torn-frontend}"
: "${DEPLOY_FRONTEND_OWNER:=root}"
: "${DEPLOY_FRONTEND_GROUP:=www-data}"
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=0}"

if [[ ! -d "${WEB_DIR}" ]]; then
  echo "web/ directory not found at ${WEB_DIR}" >&2
  exit 1
fi

readonly REMOTE_RELEASES_DIR="${DEPLOY_FRONTEND_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_FRONTEND_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/torn-frontend-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_cmd_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_cmd_pipe() { ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
  [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]] && SUDO_CMD="sudo -n" || SUDO_CMD="sudo"
else
  SUDO_CMD=""
fi

echo "==> Uploading frontend payload"
tar -C "${WEB_DIR}" -cf - . | ssh_cmd_pipe "set -euo pipefail; mkdir -p '${REMOTE_STAGING_DIR}'; rm -rf '${REMOTE_STAGING_DIR}'/*; tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating frontend release"
ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}'; \
  ${SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi; \
  ${SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  ${SUDO_CMD} chown -R '${DEPLOY_FRONTEND_OWNER}:${DEPLOY_FRONTEND_GROUP}' '${DEPLOY_FRONTEND_REMOTE_ROOT}'; \
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;; \
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;; \
  rm -rf '${REMOTE_STAGING_DIR}'"

echo "==> Frontend deployment complete"
echo "    Host: ${DEPLOY_SSH_HOST}"
echo "    Release: ${REMOTE_RELEASE_DIR}"
echo "    Current symlink: ${REMOTE_CURRENT_DIR}"