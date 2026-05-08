#!/usr/bin/env bash
# setup-adminpanel-server.sh — One-time AdminPanel systemd prerequisite setup.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly SERVICE_SRC="${ROOT_DIR}/infra/happygymstats-adminpanel.service"
readonly SUDOERS_SRC="${ROOT_DIR}/infra/sudoers-happygymstats"

[[ -f "${ROOT_DIR}/.env.deploy" ]] && source "${ROOT_DIR}/.env.deploy"
source "${SCRIPT_DIR}/deploy-config.sh"

readonly SERVICE_NAME="${DEPLOY_ADMINPANEL_SERVICE}"
readonly REMOTE_STAGE_DIR="/tmp/${SERVICE_NAME}-setup-${DEPLOY_SSH_USER}"
readonly REMOTE_STAGE_UNIT="${REMOTE_STAGE_DIR}/${SERVICE_NAME}.service"
readonly REMOTE_STAGE_SUDOERS="${REMOTE_STAGE_DIR}/sudoers-happygymstats"
readonly REMOTE_SYSTEMD_UNIT="/etc/systemd/system/${SERVICE_NAME}.service"
readonly REMOTE_SYSTEM_SUDOERS="/etc/sudoers.d/happygymstats"
readonly HEALTH_URL="http://127.0.0.1:5048/admin/health"

DRY_RUN=0

usage() {
  cat <<'USAGE'
Usage: bash scripts/setup-adminpanel-server.sh [--dry-run]

Installs/refreshes AdminPanel systemd unit on the remote host, reloads systemd,
enables and starts/restarts the service, and verifies local loopback health.

Mutation phases:
  1) upload-service-unit      - rsync service file to remote staging path
  2) upload-sudoers-policy    - rsync sudoers policy to remote staging path
  3) validate-sudoers-policy  - sudo visudo -cf staged sudoers file
  4) install-sudoers-policy   - sudo install -m 0440 to /etc/sudoers.d/happygymstats
  5) install-service-unit     - sudo install to /etc/systemd/system
  6) daemon-reload            - sudo systemctl daemon-reload
  7) enable-service           - sudo systemctl enable happygymstats-adminpanel
  8) start-or-restart-service - sudo systemctl start/restart based on current state
  9) verify-service-active    - sudo systemctl is-active happygymstats-adminpanel
 10) health-check             - classify loopback health failures (inactive/port/http)

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

upload_sudoers_policy() {
  log_phase "upload-sudoers-policy"

  if [[ ! -f "${SUDOERS_SRC}" ]]; then
    echo "Sudoers policy file missing: ${SUDOERS_SRC}" >&2
    exit 1
  fi

  if [[ "${DRY_RUN}" == "1" ]]; then
    echo "[dry-run] would rsync '${SUDOERS_SRC}' to '${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}:${REMOTE_STAGE_SUDOERS}'"
    return 0
  fi

  ssh_cmd_tty "set -euo pipefail
mkdir -p '${REMOTE_STAGE_DIR}'"

  rsync -av --delete -e "ssh ${SSH_OPTS[*]}" "${SUDOERS_SRC}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}:${REMOTE_STAGE_SUDOERS}"
}

validate_sudoers_policy() {
  log_phase "validate-sudoers-policy"
  run_remote "${SUDO_CMD} visudo -cf '${REMOTE_STAGE_SUDOERS}'"
}

install_sudoers_policy() {
  log_phase "install-sudoers-policy"
  run_remote "${SUDO_CMD} install -m 0440 '${REMOTE_STAGE_SUDOERS}' '${REMOTE_SYSTEM_SUDOERS}'"
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

verify_service_active() {
  log_phase "verify-service-active"
  run_remote "if ! ${SUDO_CMD} systemctl is-active --quiet '${SERVICE_NAME}'; then
  echo 'service-inactive: ${SERVICE_NAME}' >&2
  ${SUDO_CMD} systemctl status '${SERVICE_NAME}' --no-pager >&2 || true
  exit 1
fi

<<<<<<< Updated upstream
echo 'service-active: ${SERVICE_NAME}'"
}

health_check() {
  log_phase "health-check"
  run_remote "attempt=0
max_attempts=20
while [[ \"\${attempt}\" -lt \"\${max_attempts}\" ]]; do
  attempt=\$((attempt + 1))
  http_code=\$(curl -sS -o /dev/null -w '%{http_code}' '${HEALTH_URL}')
  curl_exit=\$?

  if [[ \"\${curl_exit}\" -eq 0 && \"\${http_code}\" -ge 200 && \"\${http_code}\" -lt 300 ]]; then
    echo 'health-check passed: ${HEALTH_URL} http='\"\${http_code}\"
    exit 0
  fi

  if [[ \"\${curl_exit}\" -eq 7 ]]; then
    if ${SUDO_CMD} systemctl is-active --quiet '${SERVICE_NAME}'; then
      echo 'health-check retry: port-unavailable url=${HEALTH_URL} attempt='\"\${attempt}\" >&2
    else
      echo 'health-check fail: service-inactive-during-loopback service=${SERVICE_NAME} attempt='\"\${attempt}\" >&2
      ${SUDO_CMD} systemctl status '${SERVICE_NAME}' --no-pager >&2 || true
      exit 1
    fi
  elif [[ \"\${curl_exit}\" -eq 0 ]]; then
    echo 'health-check retry: http-non-2xx url=${HEALTH_URL} http='\"\${http_code}\"' attempt='\"\${attempt}\" >&2
  else
    echo 'health-check retry: curl-exit url=${HEALTH_URL} exit='\"\${curl_exit}\"' attempt='\"\${attempt}\" >&2
  fi

  sleep 1
done

echo 'health-check failed: exhausted-attempts url=${HEALTH_URL}' >&2
${SUDO_CMD} systemctl status '${SERVICE_NAME}' --no-pager >&2 || true
exit 1"
}

cleanup_staging() {
  log_phase "cleanup-staging"
  run_remote "rm -rf '${REMOTE_STAGE_DIR}'"
}

=======
RUN_REMOTE_SETUP=0
CONFIRM_REMOTE_SETUP=0
DRY_RUN=0
>>>>>>> Stashed changes
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
    --dry-run)
      DRY_RUN=1
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
echo "==> Sudoers source: ${SUDOERS_SRC}"
echo "==> Sudoers destination: ${REMOTE_SYSTEM_SUDOERS}"
[[ "${DRY_RUN}" == "1" ]] && echo "==> Dry-run enabled: no remote mutations will execute"

upload_service_unit
upload_sudoers_policy
validate_sudoers_policy
install_sudoers_policy
install_service_unit
daemon_reload
enable_service
start_or_restart_service
verify_service_active
health_check
cleanup_staging

<<<<<<< Updated upstream
echo "==> AdminPanel server setup complete"
=======
cat <<EOF
==> Local preflight complete
SCRIPT_CATEGORY=manual-bootstrap
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1
    source: ${ADMIN_NGINX_SOURCE}
    remote setup host: ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}
    DEPLOY_INSTALL_ADMIN_NGINX=${DEPLOY_INSTALL_ADMIN_NGINX}
    mode: $( [[ "${DEPLOY_ADMIN_NGINX_USE_CONF_D}" == "1" ]] && echo "conf.d" || echo "sites-available/sites-enabled" )
EOF

if [[ "${DRY_RUN}" == "1" ]]; then
  echo "==> Dry-run requested; verified local/static contract and exiting before remote mutation."
  exit 0
fi

if [[ "${DEPLOY_INSTALL_ADMIN_NGINX}" != "1" ]]; then
  echo "==> Skipping remote nginx install because DEPLOY_INSTALL_ADMIN_NGINX=${DEPLOY_INSTALL_ADMIN_NGINX}."
  echo "    Set DEPLOY_INSTALL_ADMIN_NGINX=1 only after DNS/TLS is ready for admin host."
  exit 0
fi

if [[ "${RUN_REMOTE_SETUP}" != "1" || "${CONFIRM_REMOTE_SETUP}" != "1" ]]; then
  echo "==> Remote setup is gated by explicit user confirmation."
  echo "    To mutate remote nginx, re-run with: --execute --confirm-remote-setup"
  echo "    Local/static verification is allowed without those flags."
  exit 0
fi

echo "==> Staging nginx-adminpanel config to remote temp path"
cat "${ADMIN_NGINX_SOURCE}" | ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_STAGING_FILE}'"

echo "==> Installing nginx-adminpanel config (idempotent)"
if [[ "${DEPLOY_ADMIN_NGINX_USE_CONF_D}" == "1" ]]; then
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_ADMIN_NGINX_CONF_D_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_STAGING_FILE}' '${REMOTE_CONF_D_FILE}'; \
    rm -f '${REMOTE_STAGING_FILE}'"
else
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_ADMIN_NGINX_TARGET_DIR}' '${DEPLOY_ADMIN_NGINX_LINK_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_STAGING_FILE}' '${REMOTE_TARGET_FILE}'; \
    ${SUDO_CMD} ln -sfn '${REMOTE_TARGET_FILE}' '${REMOTE_LINK_FILE}'; \
    rm -f '${REMOTE_STAGING_FILE}'"
fi

echo "==> Running nginx -t"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} nginx -t"

echo "==> Reload nginx"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} systemctl reload nginx"

echo "==> AdminPanel nginx route setup complete"
>>>>>>> Stashed changes
