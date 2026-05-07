#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly SMOKE_SCRIPT="${ROOT_DIR}/scripts/verify/production-smoke.sh"
readonly DEPLOYMENT_DOC="${ROOT_DIR}/docs/DEPLOYMENT.md"

failures=0

pass() {
  printf 'PASS %s\n' "$1"
}

fail() {
  printf 'FAIL %s\n' "$1"
  failures=$((failures + 1))
}

require_file() {
  local path="$1"
  if [[ -f "${path}" ]]; then
    pass "file exists: ${path#"${ROOT_DIR}/"}"
  else
    fail "missing file: ${path#"${ROOT_DIR}/"}"
  fi
}

require_token() {
  local file="$1"
  local token="$2"
  local label="$3"
  if rg -F -q "${token}" "${file}"; then
    pass "${label}"
  else
    fail "${label} (missing token: ${token})"
  fi
}

require_executable() {
  local path="$1"
  if [[ -x "${path}" ]]; then
    pass "executable: ${path#"${ROOT_DIR}/"}"
  else
    fail "not executable: ${path#"${ROOT_DIR}/"}"
  fi
}

require_file "${SMOKE_SCRIPT}"
require_file "${DEPLOYMENT_DOC}"

if bash -n "${SMOKE_SCRIPT}"; then
  pass "bash syntax: scripts/verify/production-smoke.sh"
else
  fail "bash syntax: scripts/verify/production-smoke.sh"
fi

require_executable "${SMOKE_SCRIPT}"

# Phase contract tokens
require_token "${SMOKE_SCRIPT}" 'phase "services"' 'phase present: services'
require_token "${SMOKE_SCRIPT}" 'phase "http-routes"' 'phase present: http-routes'
require_token "${SMOKE_SCRIPT}" 'phase "containers"' 'phase present: containers'
require_token "${SMOKE_SCRIPT}" 'phase "summary"' 'phase present: summary'

# Required checks + failure boundary tokens
require_token "${SMOKE_SCRIPT}" 'check_systemd_service_required' 'systemd required checks wired'
require_token "${SMOKE_SCRIPT}" 'check_nginx_config_required' 'nginx required check wired'
require_token "${SMOKE_SCRIPT}" 'check_surfaces_latest_required' 'surfaces required check wired'
require_token "${SMOKE_SCRIPT}" 'check_http_auth_denied_required' 'admin auth-boundary check wired'
require_token "${SMOKE_SCRIPT}" 'check_container_status_optional' 'optional container checks wired'
require_token "${SMOKE_SCRIPT}" 'required_failures' 'required failure counter exists'
require_token "${SMOKE_SCRIPT}" 'optional_warnings' 'optional warning counter exists'

# Local/no-remote contract tokens
require_token "${SMOKE_SCRIPT}" ': "${SMOKE_MODE:=local}"' 'default mode is local'
require_token "${SMOKE_SCRIPT}" 'if [[ "${SMOKE_MODE}" == "remote" ]]; then' 'remote-mode branch present'
require_token "${SMOKE_SCRIPT}" 'local checks execute on host with read-only commands' 'local read-only path described'
require_token "${SMOKE_SCRIPT}" 'read-only checks only' 'usage documents read-only safety'

# Documentation contract tokens
require_token "${DEPLOYMENT_DOC}" '## Production smoke verification (S05)' 'deployment doc contains S05 section'
require_token "${DEPLOYMENT_DOC}" 'bash scripts/verify/production-smoke.sh' 'deployment doc shows smoke command'
require_token "${DEPLOYMENT_DOC}" 'SMOKE_MODE=remote' 'deployment doc shows remote mode'
require_token "${DEPLOYMENT_DOC}" 'required_failures' 'deployment doc explains required failure semantics'
require_token "${DEPLOYMENT_DOC}" 'optional_warnings' 'deployment doc explains optional warning semantics'
require_token "${DEPLOYMENT_DOC}" 'systemd-unavailable' 'deployment doc lists failure category: systemd-unavailable'
require_token "${DEPLOYMENT_DOC}" 'docker-access-unavailable' 'deployment doc lists failure category: docker-access-unavailable'

printf 'RESULT failures=%s\n' "${failures}"
if (( failures > 0 )); then
  exit 1
fi
