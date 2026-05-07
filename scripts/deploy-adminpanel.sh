#!/usr/bin/env bash
# deploy-adminpanel.sh — Publish AdminPanel and deploy to server over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

: "${DEPLOY_ADMIN_PROJECT:=${ROOT_DIR}/src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj}"
: "${DEPLOY_ADMIN_REMOTE_ROOT:=/var/www/happygymstats-adminpanel}"
: "${DEPLOY_ADMIN_REMOTE_SERVICE:=happygymstats-adminpanel}"
: "${DEPLOY_ADMIN_OWNER:=www-data}"
: "${DEPLOY_ADMIN_GROUP:=www-data}"
: "${DEPLOY_ADMIN_CONFIGURATION:=Release}"
: "${DEPLOY_ADMIN_RUNTIME:=linux-x64}"
: "${DEPLOY_ADMIN_RESTART_SERVICE:=1}"

readonly PUBLISH_DIR="${ROOT_DIR}/dist/adminpanel"
readonly REMOTE_RELEASES_DIR="${DEPLOY_ADMIN_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_ADMIN_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/happygymstats-adminpanel-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-adminpanel.sh

Publishes AdminPanel and deploys it via SSH:
  - uploads to timestamped release dir
  - flips current symlink
  - enforces release permissions
  - optionally restarts adminpanel systemd service

Preconditions (machine-checkable):
  - local AdminPanel project file exists
  - local dotnet/tar/ssh/rsync commands exist
  - remote rsync/find/systemctl commands exist
  - remote admin root is writable (directly or via configured sudo)
  - AdminPanel service unit exists before restart

If setup preconditions are missing, run one-time setup first:
  bash scripts/setup-adminpanel-server.sh --help
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  deploy_print_common_connection_summary
  exit 0
fi

echo "==> Running AdminPanel deploy preconditions"
deploy_precheck_require_local_file "${DEPLOY_ADMIN_PROJECT}" "missing_adminpanel_project"
deploy_precheck_require_local_command dotnet
deploy_precheck_require_local_command tar
deploy_precheck_require_local_command ssh
deploy_precheck_require_local_command rsync
deploy_precheck_remote_sudo_access
deploy_precheck_remote_command rsync
deploy_precheck_remote_command find
deploy_precheck_remote_command systemctl
deploy_precheck_remote_root_ready "${DEPLOY_ADMIN_REMOTE_ROOT}"
if [[ "${DEPLOY_ADMIN_RESTART_SERVICE}" == "1" ]]; then
  deploy_precheck_remote_service_exists "${DEPLOY_ADMIN_REMOTE_SERVICE}" "run scripts/setup-adminpanel-server.sh --help"
fi

echo "==> Publishing AdminPanel"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${DEPLOY_ADMIN_PROJECT}" -c "${DEPLOY_ADMIN_CONFIGURATION}" -r "${DEPLOY_ADMIN_RUNTIME}" --self-contained true -o "${PUBLISH_DIR}"

echo "==> Uploading payload"
tar -C "${PUBLISH_DIR}" -cf - . | deploy_ssh_pipe "set -euo pipefail; mkdir -p '${REMOTE_STAGING_DIR}'; rm -rf '${REMOTE_STAGING_DIR}'/*; tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating AdminPanel release"
deploy_ssh_tty "set -euo pipefail; \
  ${DEPLOY_SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}'; \
  ${DEPLOY_SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${DEPLOY_SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi; \
  ${DEPLOY_SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  ${DEPLOY_SUDO_CMD} chown -R '${DEPLOY_ADMIN_OWNER}:${DEPLOY_ADMIN_GROUP}' '${REMOTE_RELEASE_DIR}'; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;; \
  ${DEPLOY_SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;; \
  rm -rf '${REMOTE_STAGING_DIR}'"

if [[ "${DEPLOY_ADMIN_RESTART_SERVICE}" == "1" ]]; then
  echo "==> Restarting service ${DEPLOY_ADMIN_REMOTE_SERVICE}"
  deploy_ssh_tty "set -euo pipefail; ${DEPLOY_SUDO_CMD} systemctl restart '${DEPLOY_ADMIN_REMOTE_SERVICE}'; ${DEPLOY_SUDO_CMD} systemctl --no-pager --full status '${DEPLOY_ADMIN_REMOTE_SERVICE}' | head -n 25"
else
  echo "==> Skipping service restart"
fi

echo "==> AdminPanel release activation complete"
deploy_print_post_deploy_smoke_next_step
