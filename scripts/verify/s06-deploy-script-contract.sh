#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly SCRIPTS_DIR="${ROOT_DIR}/scripts"

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

require_bash_syntax() {
  local file="$1"
  if bash -n "${file}"; then
    pass "bash syntax: ${file#"${ROOT_DIR}/"}"
  else
    fail "bash syntax: ${file#"${ROOT_DIR}/"}"
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

readonly DEPLOY_CONFIG="${SCRIPTS_DIR}/deploy-config.sh"
readonly DEPLOY_ORCHESTRATOR="${SCRIPTS_DIR}/deploy.sh"
readonly DEPLOY_BACKEND="${SCRIPTS_DIR}/deploy-backend.sh"
readonly DEPLOY_FRONTEND="${SCRIPTS_DIR}/deploy-frontend.sh"
readonly DEPLOY_ADMINPANEL="${SCRIPTS_DIR}/deploy-adminpanel.sh"
readonly DEPLOY_CONTAINERS="${SCRIPTS_DIR}/deploy-containers.sh"
readonly SETUP_ADMINPANEL="${SCRIPTS_DIR}/setup-adminpanel-server.sh"
readonly SMOKE_SCRIPT="${SCRIPTS_DIR}/verify/production-smoke.sh"

require_file "${DEPLOY_CONFIG}"
require_file "${DEPLOY_ORCHESTRATOR}"
require_file "${DEPLOY_BACKEND}"
require_file "${DEPLOY_FRONTEND}"
require_file "${DEPLOY_ADMINPANEL}"
require_file "${DEPLOY_CONTAINERS}"
require_file "${SETUP_ADMINPANEL}"
require_file "${SMOKE_SCRIPT}"

require_executable "${DEPLOY_ORCHESTRATOR}"
require_executable "${DEPLOY_BACKEND}"
require_executable "${DEPLOY_FRONTEND}"
require_executable "${DEPLOY_ADMINPANEL}"
require_executable "${DEPLOY_CONTAINERS}"
require_executable "${SMOKE_SCRIPT}"

# 1) Syntax contract: all deploy/setup/smoke scripts must parse.
require_bash_syntax "${DEPLOY_CONFIG}"
require_bash_syntax "${DEPLOY_ORCHESTRATOR}"
require_bash_syntax "${DEPLOY_BACKEND}"
require_bash_syntax "${DEPLOY_FRONTEND}"
require_bash_syntax "${DEPLOY_ADMINPANEL}"
require_bash_syntax "${DEPLOY_CONTAINERS}"
require_bash_syntax "${SETUP_ADMINPANEL}"
require_bash_syntax "${SMOKE_SCRIPT}"

# 2) No duplicate hardcoded SSH literals outside shared deploy config,
#    except explicitly allowlisted bootstrap/smoke scripts.
readonly SSH_LITERAL_ALLOWLIST_REGEX="scripts/deploy-config.sh|scripts/verify/s06-deploy-script-contract.sh|scripts/setup-adminpanel-server.sh|scripts/verify/production-smoke.sh"

ssh_dup_hits="$({
  rg -n -F "ssh.geromet.com" "${SCRIPTS_DIR}" || true
  rg -n -F "id_token2_bio3_hetzner" "${SCRIPTS_DIR}" || true
  rg -n -F "cloudflared access ssh --hostname ssh.geromet.com" "${SCRIPTS_DIR}" || true
} | rg -v "${SSH_LITERAL_ALLOWLIST_REGEX}" || true)"

if [[ -z "${ssh_dup_hits}" ]]; then
  pass "no duplicated hardcoded SSH proxy/key literals outside allowlisted scripts"
else
  fail "duplicated hardcoded SSH literals detected outside allowlisted scripts"
  printf '%s\n' "${ssh_dup_hits}" >&2
fi

# 3) Release/symlink activation tokens required in API/Blazor/Admin deploy scripts.
require_token "${DEPLOY_BACKEND}" "REMOTE_RELEASES_DIR" "backend release dir token present"
require_token "${DEPLOY_BACKEND}" "REMOTE_CURRENT_DIR" "backend current symlink token present"
require_token "${DEPLOY_BACKEND}" "ln -sfn" "backend symlink activation command present"

require_token "${DEPLOY_FRONTEND}" "REMOTE_RELEASES_DIR" "frontend release dir token present"
require_token "${DEPLOY_FRONTEND}" "REMOTE_CURRENT_DIR" "frontend current symlink token present"
require_token "${DEPLOY_FRONTEND}" "ln -sfn" "frontend symlink activation command present"

require_token "${DEPLOY_ADMINPANEL}" "REMOTE_RELEASES_DIR" "adminpanel release dir token present"
require_token "${DEPLOY_ADMINPANEL}" "REMOTE_CURRENT_DIR" "adminpanel current symlink token present"
require_token "${DEPLOY_ADMINPANEL}" "ln -sfn" "adminpanel symlink activation command present"

# 4) AdminPanel deploy must include missing-service setup guidance.
require_token "${DEPLOY_ADMINPANEL}" "run scripts/setup-adminpanel-server.sh --help" "adminpanel missing-service setup hint present"

# 5) Smoke hook/reference contract must remain wired.
require_token "${DEPLOY_CONFIG}" "deploy_print_post_deploy_smoke_next_step" "shared smoke next-step helper present"
require_token "${DEPLOY_CONFIG}" "deploy_run_post_deploy_smoke_if_enabled" "shared smoke runner helper present"
require_token "${DEPLOY_ORCHESTRATOR}" "deploy_run_post_deploy_smoke_if_enabled" "orchestrator invokes smoke runner"
require_token "${DEPLOY_ORCHESTRATOR}" "DEPLOY_RUN_SMOKE=1" "orchestrator usage documents smoke toggle"
require_token "${DEPLOY_BACKEND}" "deploy_print_post_deploy_smoke_next_step" "backend deploy prints smoke next step"
require_token "${DEPLOY_FRONTEND}" "deploy_print_post_deploy_smoke_next_step" "frontend deploy prints smoke next step"
require_token "${DEPLOY_ADMINPANEL}" "deploy_print_post_deploy_smoke_next_step" "adminpanel deploy prints smoke next step"

printf 'RESULT failures=%s\n' "${failures}"
if (( failures > 0 )); then
  exit 1
fi
