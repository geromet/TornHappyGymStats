#!/usr/bin/env bash
# setup-adminpanel-server.sh — One-time AdminPanel systemd prerequisite setup.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly SERVICE_SRC="${ROOT_DIR}/infra/happygymstats-adminpanel.service"

[[ -f "${ROOT_DIR}/.env.deploy" ]] && source "${ROOT_DIR}/.env.deploy"
source "${SCRIPT_DIR}/deploy-config.sh"

readonly SERVICE_NAME="${DEPLOY_ADMINPANEL_SERVICE}"
readonly REMOTE_STAGE_DIR="/tmp/${SERVICE_NAME}-setup-${DEPLOY_SSH_USER}"
readonly REMOTE_STAGE_UNIT="${REMOTE_STAGE_DIR}/${SERVICE_NAME}.service"
readonly REMOTE_SYSTEMD_UNIT="/etc/systemd/system/${SERVICE_NAME}.service"
readonly HEALTH_URL="http://127.0.0.1:5048/admin/health"

DRY_RUN=0

usage() {
  cat <<'USAGE'
Usage: bash scripts/setup-adminpanel-server.sh [--dry-run]

Installs/refreshes AdminPanel systemd unit on the remote host, reloads systemd,
enables and starts/restarts the service, and verifies local loopback health.

Mutation phases:
  1) upload-service-unit      - rsync service file to remote staging path
  2) install-service-unit     - sudo install to /etc/systemd/system
  3) daemon-reload            - sudo systemctl daemon-reload
  4) enable-service           - sudo systemctl enable happygymstats-adminpanel
  5) start-or-restart-service - sudo systemctl start/restart based on current state
  6) health-check             - curl http://127.0.0.1:5048/admin/health until success

Flags:
  -n, --dry-run   Print planned remote mutations without executing them
  -h, --help      Show this help text
USAGE
}

log_phase() {
  echo "==> phase: $1"
}

run_remote() {
  local remote_script="$1"
  if [[ "${DRY_RUN}" == "1" ]]; then
    echo "[dry-run] would run remote script:"
    printf '%s\n' "${remote_script}"
    return 0
  fi
  ssh_cmd_tty "set -euo pipefail
${remote_script}"
}

upload_service_unit() {
  log_phase "upload-service-unit"

  if [[ ! -f "${SERVICE_SRC}" ]]; then
    echo "Service unit file missing: ${SERVICE_SRC}" >&2
    exit 1
  fi

  if [[ "${DRY_RUN}" == "1" ]]; then
    echo "[dry-run] would rsync '${SERVICE_SRC}' to '${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}:${REMOTE_STAGE_UNIT}'"
    return 0
  fi

  ssh_cmd_tty "set -euo pipefail
mkdir -p '${REMOTE_STAGE_DIR}'"

  rsync -av --delete -e "ssh ${SSH_OPTS[*]}" "${SERVICE_SRC}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}:${REMOTE_STAGE_UNIT}"
}

install_service_unit() {
  log_phase "install-service-unit"
  run_remote "${SUDO_CMD} install -m 0644 '${REMOTE_STAGE_UNIT}' '${REMOTE_SYSTEMD_UNIT}'"
}

daemon_reload() {
  log_phase "daemon-reload"
  run_remote "${SUDO_CMD} systemctl daemon-reload"
}

enable_service() {
  log_phase "enable-service"
  run_remote "${SUDO_CMD} systemctl enable '${SERVICE_NAME}'"
}

start_or_restart_service() {
  log_phase "start-or-restart-service"
  run_remote "if ${SUDO_CMD} systemctl is-active --quiet '${SERVICE_NAME}'; then
  ${SUDO_CMD} systemctl restart '${SERVICE_NAME}'
else
  ${SUDO_CMD} systemctl start '${SERVICE_NAME}'
fi"
}

health_check() {
  log_phase "health-check"
  run_remote "attempt=0
max_attempts=20
until curl -fsS '${HEALTH_URL}' >/dev/null 2>&1; do
  attempt=\$((attempt + 1))
  if [[ \"\${attempt}\" -ge \"\${max_attempts}\" ]]; then
    echo 'health-check failed for ${HEALTH_URL}' >&2
    exit 1
  fi
  sleep 1
done

echo 'health-check passed: ${HEALTH_URL}'"
}

cleanup_staging() {
  log_phase "cleanup-staging"
  run_remote "rm -rf '${REMOTE_STAGE_DIR}'"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    -n|--dry-run)
      DRY_RUN=1
      shift
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

echo "==> AdminPanel setup target: ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
echo "==> Service unit source: ${SERVICE_SRC}"
echo "==> Service unit destination: ${REMOTE_SYSTEMD_UNIT}"
[[ "${DRY_RUN}" == "1" ]] && echo "==> Dry-run enabled: no remote mutations will execute"

upload_service_unit
install_service_unit
daemon_reload
enable_service
start_or_restart_service
health_check
cleanup_staging

echo "==> AdminPanel server setup complete"
