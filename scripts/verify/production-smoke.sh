#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_NAME="$(basename "$0")"
readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# Optional config source (non-fatal if missing in local/dev checkouts).
readonly DEPLOY_CONFIG_PATH="${ROOT_DIR}/scripts/deploy-config.sh"
if [[ -f "${DEPLOY_CONFIG_PATH}" ]]; then
  # shellcheck disable=SC1090
  source "${DEPLOY_CONFIG_PATH}"
fi

: "${SMOKE_MODE:=local}"
: "${SMOKE_SSH_HOST:=ssh.geromet.com}"
: "${SMOKE_SSH_USER:=anon}"
: "${SMOKE_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${SMOKE_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${SMOKE_TIMEOUT_SECONDS:=8}"

required_failures=0
optional_warnings=0

usage() {
  cat <<EOF
Usage: bash scripts/verify/production-smoke.sh [--help]

Production smoke framework for read-only checks.

Mode:
  - local (default): execute checks on the current machine.
  - remote: execute checks over SSH with read-only commands.

Environment overrides:
  SMOKE_MODE              local|remote (default: ${SMOKE_MODE})
  SMOKE_TIMEOUT_SECONDS   Curl/command timeout in seconds (default: ${SMOKE_TIMEOUT_SECONDS})
  SMOKE_SSH_HOST          Remote SSH host for remote mode (default: ${SMOKE_SSH_HOST})
  SMOKE_SSH_USER          Remote SSH user for remote mode (default: ${SMOKE_SSH_USER})
  SMOKE_SSH_KEY           SSH key path for remote mode (default: ${SMOKE_SSH_KEY})
  SMOKE_PROXY_COMMAND     SSH ProxyCommand for remote mode (default: ${SMOKE_PROXY_COMMAND})

Exit semantics:
  - required check failure => non-zero exit
  - optional check unavailable/failure => WARN only

Safety:
  - read-only checks only
  - never prints secret values, TOKENs, KEYs, or env file contents
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

phase() {
  local name="$1"
  printf "\n== PHASE: %s ==\n" "${name}"
}

pass() {
  local kind="$1"
  local msg="$2"
  printf "PASS [%s] %s\n" "${kind}" "${msg}"
}

warn() {
  local kind="$1"
  local msg="$2"
  printf "WARN [%s] %s\n" "${kind}" "${msg}"
  optional_warnings=$((optional_warnings + 1))
}

fail() {
  local kind="$1"
  local msg="$2"
  printf "FAIL [%s] %s\n" "${kind}" "${msg}"
  required_failures=$((required_failures + 1))
}

smoke_ssh() {
  ssh -i "${SMOKE_SSH_KEY}" -o "ProxyCommand=${SMOKE_PROXY_COMMAND}" "${SMOKE_SSH_USER}@${SMOKE_SSH_HOST}" "$@"
}

run_required() {
  local label="$1"
  local cmd="$2"

  if eval "${cmd}" >/dev/null 2>&1; then
    pass "required" "${label}"
  else
    fail "required" "${label}"
  fi
}

run_optional() {
  local label="$1"
  local cmd="$2"

  if eval "${cmd}" >/dev/null 2>&1; then
    pass "optional" "${label}"
  else
    warn "optional" "${label}"
  fi
}

http_status() {
  local url="$1"
  local timeout="$2"

  set +e
  local out
  out="$(curl -sS -o /dev/null -w "%{http_code} %{exitcode}" --max-time "${timeout}" "${url}" 2>/dev/null)"
  local rc=$?
  set -e

  local code="000"
  local curl_exit="1"
  if [[ -n "${out}" ]]; then
    code="${out%% *}"
    curl_exit="${out##* }"
  fi
  if [[ ${rc} -ne 0 && "${curl_exit}" == "0" ]]; then
    curl_exit="${rc}"
  fi

  printf "%s|%s\n" "${code}" "${curl_exit}"
}

check_http_required() {
  local label="$1"
  local url="$2"
  local expected_regex="$3"

  local result
  result="$(http_status "${url}" "${SMOKE_TIMEOUT_SECONDS}")"
  local code="${result%%|*}"
  local curl_exit="${result##*|}"

  if [[ "${curl_exit}" != "0" ]]; then
    fail "required" "${label}: curl_exit=${curl_exit} status=${code} url=${url}"
    return
  fi

  if [[ "${code}" =~ ${expected_regex} ]]; then
    pass "required" "${label}: status=${code} url=${url}"
  else
    fail "required" "${label}: status=${code} expected=${expected_regex} url=${url}"
  fi
}

check_http_optional() {
  local label="$1"
  local url="$2"
  local expected_regex="$3"

  local result
  result="$(http_status "${url}" "${SMOKE_TIMEOUT_SECONDS}")"
  local code="${result%%|*}"
  local curl_exit="${result##*|}"

  if [[ "${curl_exit}" != "0" ]]; then
    warn "optional" "${label}: curl_exit=${curl_exit} status=${code} url=${url}"
    return
  fi

  if [[ "${code}" =~ ${expected_regex} ]]; then
    pass "optional" "${label}: status=${code} url=${url}"
  else
    warn "optional" "${label}: status=${code} expected=${expected_regex} url=${url}"
  fi
}

phase "framework"
pass "required" "mode=${SMOKE_MODE}"

if [[ "${SMOKE_MODE}" == "remote" ]]; then
  phase "remote-preflight"
  run_required "ssh connectivity" "smoke_ssh 'echo smoke-remote-ok'"
  pass "required" "remote checks execute over SSH in read-only mode"
else
  pass "required" "local checks execute on host with read-only commands"
fi

phase "summary"
printf "RESULT required_failures=%s optional_warnings=%s\n" "${required_failures}" "${optional_warnings}"

if (( required_failures > 0 )); then
  exit 1
fi

exit 0
