#!/usr/bin/env bash
# deploy-frontend.sh — Deploy web/ to torn.geromet.com host over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"
readonly WEB_DIR="${ROOT_DIR}/web"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

: "${DEPLOY_FRONTEND_REMOTE_ROOT:=/var/www/torn-frontend}"
: "${DEPLOY_FRONTEND_OWNER:=root}"
: "${DEPLOY_FRONTEND_GROUP:=www-data}"

readonly REMOTE_RELEASES_DIR="${DEPLOY_FRONTEND_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_FRONTEND_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/torn-frontend-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-frontend.sh

Deploys local web/ assets to the remote host:
  - uploads to timestamped release dir
  - flips current symlink
  - enforces frontend permissions

Preconditions (machine-checkable):
  - local web/ directory exists
  - local tar/ssh/rsync commands exist
  - remote rsync/find commands exist
  - remote frontend root is writable (directly or via configured sudo)
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  deploy_print_common_connection_summary
  exit 0
fi

echo "==> Running frontend deploy preconditions"
echo "==> Runtime contract: frontend is static assets (dotnet runtime not required on host)"
deploy_precheck_require_local_dir "${WEB_DIR}" "missing_frontend_directory"
deploy_precheck_require_local_command tar
deploy_precheck_require_local_command ssh
deploy_precheck_require_local_command rsync
deploy_precheck_remote_sudo_access
deploy_precheck_remote_command rsync
deploy_precheck_remote_command find
deploy_precheck_remote_root_ready "${DEPLOY_FRONTEND_REMOTE_ROOT}"

echo "==> Uploading frontend payload"
tar -C "${WEB_DIR}" -cf - . | deploy_ssh_pipe "set -euo pipefail; mkdir -p '${REMOTE_STAGING_DIR}'; rm -rf '${REMOTE_STAGING_DIR}'/*; tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating frontend release"
deploy_ssh_tty "set -euo pipefail; \
  ${DEPLOY_SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}'; \
  ${DEPLOY_SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${DEPLOY_SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi; \
  ${DEPLOY_SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  ${DEPLOY_SUDO_CMD} chown -R '${DEPLOY_FRONTEND_OWNER}:${DEPLOY_FRONTEND_GROUP}' '${DEPLOY_FRONTEND_REMOTE_ROOT}'; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;; \
  rm -rf '${REMOTE_STAGING_DIR}'"

echo "==> Frontend release activation complete"
echo "    Host: ${DEPLOY_SSH_HOST}"
echo "    Release: ${REMOTE_RELEASE_DIR}"
echo "    Current symlink: ${REMOTE_CURRENT_DIR}"
deploy_print_post_deploy_smoke_next_step
