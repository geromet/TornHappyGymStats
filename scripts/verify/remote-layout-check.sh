#!/usr/bin/env bash
set -euo pipefail

# Read-only remote layout check before changing deploy roots.
# Uses deploy-config SSH settings and does not mutate remote state.

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly DEPLOY_CONFIG_PATH="${ROOT_DIR}/scripts/deploy-config.sh"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

: "${LAYOUT_API_ROOT:=/var/www/happygymstats}"
: "${LAYOUT_BLAZOR_ROOT:=/var/www/happygymstats-blazor}"
: "${LAYOUT_ADMIN_ROOT:=/var/www/happygymstats-adminpanel}"

: "${LAYOUT_API_SERVICE:=happygymstats-api}"
: "${LAYOUT_BLAZOR_SERVICE:=happygymstats-blazor}"
: "${LAYOUT_ADMIN_SERVICE:=happygymstats-adminpanel}"

usage() {
  cat <<EOF
Usage: bash scripts/verify/remote-layout-check.sh

SCRIPT_CATEGORY=diagnostic-read-only
SCRIPT_MUTATES_SERVER_STATE=0
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Checks remote host layout for expected deploy roots, symlinks, and service presence.
No remote mutation is performed.

Environment overrides:
  LAYOUT_API_ROOT        (default: ${LAYOUT_API_ROOT})
  LAYOUT_BLAZOR_ROOT     (default: ${LAYOUT_BLAZOR_ROOT})
  LAYOUT_ADMIN_ROOT      (default: ${LAYOUT_ADMIN_ROOT})

  LAYOUT_API_SERVICE     (default: ${LAYOUT_API_SERVICE})
  LAYOUT_BLAZOR_SERVICE  (default: ${LAYOUT_BLAZOR_SERVICE})
  LAYOUT_ADMIN_SERVICE   (default: ${LAYOUT_ADMIN_SERVICE})
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

failures=0
warnings=0

pass() {
  printf 'PASS [required] %s\n' "$1"
}

warn() {
  printf 'WARN [optional] %s\n' "$1"
  warnings=$((warnings + 1))
}

fail() {
  printf 'FAIL [required] %s\n' "$1"
  failures=$((failures + 1))
}

phase() {
  printf '\n== PHASE: %s ==\n' "$1"
}

remote_exec() {
  deploy_ssh_tty "$1"
}

check_root() {
  local label="$1"
  local path="$2"

  if remote_exec "set -euo pipefail; [[ -d '${path}' ]]" >/dev/null 2>&1; then
    pass "${label}: root exists (${path})"
  else
    fail "${label}: root missing (${path})"
    return
  fi

  if remote_exec "set -euo pipefail; [[ -L '${path}/current' ]]" >/dev/null 2>&1; then
    local target
    target="$(remote_exec "set -euo pipefail; readlink -f '${path}/current'" 2>/dev/null || true)"
    pass "${label}: current symlink present (${path}/current -> ${target})"
  elif remote_exec "set -euo pipefail; [[ -d '${path}/current' ]]" >/dev/null 2>&1; then
    warn "${label}: current exists but is not symlink (${path}/current)"
  else
    warn "${label}: current missing (${path}/current)"
  fi

  if remote_exec "set -euo pipefail; [[ -d '${path}/releases' ]]" >/dev/null 2>&1; then
    pass "${label}: releases directory present (${path}/releases)"
  else
    warn "${label}: releases directory missing (${path}/releases)"
  fi
}

check_service() {
  local service="$1"
  local unit="${service}.service"

  if remote_exec "set -euo pipefail; systemctl list-unit-files --type=service --all | awk '{print \$1}' | grep -Fx '${unit}' >/dev/null" >/dev/null 2>&1; then
    pass "service unit present (${unit})"
  else
    fail "service unit missing (${unit})"
    return
  fi

  local state
  state="$(remote_exec "set -euo pipefail; systemctl is-active '${unit}' 2>/dev/null || true" 2>/dev/null || true)"
  if [[ -n "${state}" ]]; then
    pass "service state (${unit})=${state}"
  else
    warn "service state unavailable (${unit})"
  fi
}

phase "connection"
pass "target host=${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
remote_exec "set -euo pipefail; echo REMOTE_OK" >/dev/null 2>&1 || {
  fail "unable to connect to remote host"
  printf '\nRESULT required_failures=%s optional_warnings=%s\n' "$failures" "$warnings"
  exit 1
}
pass "remote SSH connectivity"

phase "roots"
check_root "api" "${LAYOUT_API_ROOT}"
check_root "blazor" "${LAYOUT_BLAZOR_ROOT}"
check_root "adminpanel" "${LAYOUT_ADMIN_ROOT}"
check_root "frontend" "${LAYOUT_FRONTEND_ROOT}"

phase "services"
check_service "${LAYOUT_API_SERVICE}"
check_service "${LAYOUT_BLAZOR_SERVICE}"
check_service "${LAYOUT_ADMIN_SERVICE}"

phase "summary"
printf 'RESULT required_failures=%s optional_warnings=%s\n' "$failures" "$warnings"

if (( failures > 0 )); then
  exit 1
fi

exit 0

fi

exit 0
