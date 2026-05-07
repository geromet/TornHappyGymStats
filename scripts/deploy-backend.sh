#!/usr/bin/env bash
# deploy-backend.sh — Publish API and deploy to server over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

: "${DEPLOY_REMOTE_ROOT:=/var/www/happygymstats}"
: "${DEPLOY_REMOTE_SERVICE:=happygymstats-api}"
: "${DEPLOY_BACKEND_OWNER:=www-data}"
: "${DEPLOY_BACKEND_GROUP:=www-data}"
: "${DEPLOY_CONFIGURATION:=Release}"
: "${DEPLOY_RUNTIME:=linux-x64}"
: "${DEPLOY_RESTART_SERVICE:=1}"

readonly PUBLISH_DIR="${ROOT_DIR}/dist/backend-api"
readonly REMOTE_RELEASES_DIR="${DEPLOY_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/happygymstats-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-backend.sh

Publishes HappyGymStats.Api and deploys it via SSH:
  - uploads to timestamped release dir
  - flips current symlink
  - enforces release/data permissions
  - optionally restarts systemd service

Preconditions (machine-checkable):
  - local API project file exists
  - local dotnet, tar, ssh, and rsync commands exist
  - remote rsync/find/systemctl commands exist
  - remote deployment root is writable (directly or via configured sudo)
  - systemd service unit exists before restart
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  deploy_print_common_connection_summary
  exit 0
fi

echo "==> Running backend deploy preconditions"
deploy_precheck_require_local_file "${API_PROJECT}" "missing_backend_project"
deploy_precheck_require_local_command dotnet
deploy_precheck_require_local_command tar
deploy_precheck_require_local_command ssh
deploy_precheck_require_local_command rsync
deploy_precheck_remote_sudo_access
deploy_precheck_remote_command rsync
deploy_precheck_remote_command find
deploy_precheck_remote_command systemctl
deploy_precheck_remote_root_ready "${DEPLOY_REMOTE_ROOT}"
if [[ "${DEPLOY_RESTART_SERVICE}" == "1" ]]; then
  deploy_precheck_remote_service_exists "${DEPLOY_REMOTE_SERVICE}"
fi

echo "==> Publishing API"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${API_PROJECT}" -c "${DEPLOY_CONFIGURATION}" -r "${DEPLOY_RUNTIME}" --self-contained true -o "${PUBLISH_DIR}"

echo "==> Uploading payload"
tar -C "${PUBLISH_DIR}" -cf - . | deploy_ssh_pipe "set -euo pipefail; mkdir -p '${REMOTE_STAGING_DIR}'; rm -rf '${REMOTE_STAGING_DIR}'/*; tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating backend release"
deploy_ssh_tty "set -euo pipefail; \
  ${DEPLOY_SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_REMOTE_ROOT}/data'; \
  ${DEPLOY_SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${DEPLOY_SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi; \
  ${DEPLOY_SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  ${DEPLOY_SUDO_CMD} chown -R '${DEPLOY_BACKEND_OWNER}:${DEPLOY_BACKEND_GROUP}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_REMOTE_ROOT}/data'; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;; \
  if [[ -f '${REMOTE_RELEASE_DIR}/HappyGymStats.Api' ]]; then ${DEPLOY_SUDO_CMD} chmod 755 '${REMOTE_RELEASE_DIR}/HappyGymStats.Api'; fi; \
  rm -rf '${REMOTE_STAGING_DIR}'"

if [[ "${DEPLOY_RESTART_SERVICE}" == "1" ]]; then
  echo "==> Restarting service ${DEPLOY_REMOTE_SERVICE}"
  deploy_ssh_tty "set -euo pipefail; ${DEPLOY_SUDO_CMD} systemctl restart '${DEPLOY_REMOTE_SERVICE}'; ${DEPLOY_SUDO_CMD} systemctl --no-pager --full status '${DEPLOY_REMOTE_SERVICE}' | head -n 25"
else
  echo "==> Skipping service restart"
fi

echo "==> Backend release activation complete"
deploy_print_post_deploy_smoke_next_step
