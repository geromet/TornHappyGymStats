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
: "${SMOKE_SERVICE_API:=happygymstats-api}"
: "${SMOKE_SERVICE_BLAZOR:=happygymstats-blazor}"
: "${SMOKE_SERVICE_ADMINPANEL:=happygymstats-adminpanel}"
: "${SMOKE_API_PORT:=5047}"
: "${SMOKE_BLAZOR_PORT:=5182}"
: "${SMOKE_ADMINPANEL_PORT:=5048}"
: "${SMOKE_API_LOOPBACK_HEALTH_URL:=http://127.0.0.1:${SMOKE_API_PORT}/api/v1/torn/health}"
: "${SMOKE_API_EXTERNAL_HEALTH_URL:=https://torn.geromet.com/api/v1/torn/health}"
: "${SMOKE_SURFACES_LATEST_URL:=https://torn.geromet.com/api/v1/torn/surfaces/latest}"
: "${SMOKE_BLAZOR_HOME_URL:=https://torn.geromet.com/}"
: "${SMOKE_ADMIN_LOOPBACK_HEALTH_URL:=http://127.0.0.1:${SMOKE_ADMINPANEL_PORT}/admin/health}"
: "${SMOKE_ADMIN_EXTERNAL_HEALTH_URL:=https://admin.geromet.com/admin/health}"
: "${SMOKE_ADMIN_PROTECTED_URL:=https://admin.geromet.com/admin/api/v1/import-runs}"
: "${SMOKE_POSTGRES_CONTAINER_HINT:=postgres}"
: "${SMOKE_KEYCLOAK_CONTAINER_HINT:=keycloak}"

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
  SMOKE_API_LOOPBACK_HEALTH_URL   Loopback API health URL
  SMOKE_API_EXTERNAL_HEALTH_URL   External API health URL
  SMOKE_SURFACES_LATEST_URL       External surfaces/latest URL
  SMOKE_BLAZOR_HOME_URL           External Blazor home URL
  SMOKE_ADMIN_LOOPBACK_HEALTH_URL Loopback AdminPanel health URL
  SMOKE_ADMIN_EXTERNAL_HEALTH_URL External AdminPanel health URL
  SMOKE_ADMIN_PROTECTED_URL       Protected AdminPanel endpoint URL
  SMOKE_POSTGRES_CONTAINER_HINT   Postgres container name/image hint (default: ${SMOKE_POSTGRES_CONTAINER_HINT})
  SMOKE_KEYCLOAK_CONTAINER_HINT   Keycloak container name/image hint (default: ${SMOKE_KEYCLOAK_CONTAINER_HINT})

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

sanitize_excerpt() {
  local input="$1"
  printf '%s' "${input}" | tr '\n\r\t' '   ' | sed -E 's/[[:space:]]+/ /g' | cut -c1-180
}

http_probe_host() {
  local url="$1"
  local timeout="$2"
  local cmd="curl -sS --max-time ${timeout} -w '||HTTP:%{http_code}||CURL:%{exitcode}' '${url}'"

  set +e
  local out
  out="$(run_host_capture "${cmd}" 2>/dev/null)"
  local rc=$?
  set -e

  local http_code="000"
  local curl_exit="1"
  local body=""

  if [[ "${out}" == *"||HTTP:"*"||CURL:"* ]]; then
    body="${out%%||HTTP:*}"
    local tail="${out##*||HTTP:}"
    http_code="${tail%%||CURL:*}"
    curl_exit="${tail##*||CURL:}"
  elif [[ -n "${out}" ]]; then
    body="${out}"
  fi

  if [[ ${rc} -ne 0 && "${curl_exit}" == "0" ]]; then
    curl_exit="${rc}"
  fi

  printf '%s|%s|%s\n' "${http_code}" "${curl_exit}" "$(sanitize_excerpt "${body}")"
}

check_http_required_host() {
  local label="$1"
  local url="$2"
  local expected_regex="$3"

  local result
  result="$(http_probe_host "${url}" "${SMOKE_TIMEOUT_SECONDS}")"
  local code="${result%%|*}"
  local remainder="${result#*|}"
  local curl_exit="${remainder%%|*}"
  local excerpt="${remainder#*|}"

  if [[ "${curl_exit}" != "0" ]]; then
    fail "required" "${label}: curl_exit=${curl_exit} status=${code} url=${url} excerpt='${excerpt}'"
    return
  fi

  if [[ "${code}" =~ ${expected_regex} ]]; then
    pass "required" "${label}: status=${code} url=${url}"
  else
    fail "required" "${label}: status=${code} expected=${expected_regex} url=${url} excerpt='${excerpt}'"
  fi
}

check_surfaces_latest_required() {
  local label="$1"
  local url="$2"

  local result
  result="$(http_probe_host "${url}" "${SMOKE_TIMEOUT_SECONDS}")"
  local code="${result%%|*}"
  local remainder="${result#*|}"
  local curl_exit="${remainder%%|*}"
  local excerpt="${remainder#*|}"

  if [[ "${curl_exit}" != "0" ]]; then
    fail "required" "${label}: curl_exit=${curl_exit} status=${code} url=${url} excerpt='${excerpt}'"
    return
  fi

  if [[ "${code}" == "502" ]]; then
    fail "required" "${label}: status=502 (Bad Gateway) url=${url} excerpt='${excerpt}'"
    return
  fi

  if [[ "${code}" == "200" ]]; then
    if [[ "${excerpt}" == *"{"* || "${excerpt}" == *"["* ]]; then
      pass "required" "${label}: status=200 json-like-body url=${url}"
    else
      fail "required" "${label}: status=200 non-json-body url=${url} excerpt='${excerpt}'"
    fi
    return
  fi

  if [[ "${code}" == "404" ]]; then
    if [[ "${excerpt}" == *"cache"* || "${excerpt}" == *"not found"* || "${excerpt}" == *"no data"* || "${excerpt}" == *"{"* ]]; then
      pass "required" "${label}: status=404 structured-no-cache url=${url}"
    else
      fail "required" "${label}: status=404 unstructured-body url=${url} excerpt='${excerpt}'"
    fi
    return
  fi

  fail "required" "${label}: status=${code} expected=200_or_404 url=${url} excerpt='${excerpt}'"
}

check_http_auth_denied_required() {
  local label="$1"
  local url="$2"

  local result
  result="$(http_probe_host "${url}" "${SMOKE_TIMEOUT_SECONDS}")"
  local code="${result%%|*}"
  local remainder="${result#*|}"
  local curl_exit="${remainder%%|*}"
  local excerpt="${remainder#*|}"

  if [[ "${curl_exit}" != "0" ]]; then
    fail "required" "${label}: curl_exit=${curl_exit} status=${code} url=${url} excerpt='${excerpt}'"
    return
  fi

  if [[ "${code}" == "502" ]]; then
    fail "required" "${label}: status=502 (Bad Gateway) url=${url} excerpt='${excerpt}'"
  elif [[ "${code}" == "401" || "${code}" == "403" ]]; then
    pass "required" "${label}: status=${code} auth-denied url=${url}"
  else
    fail "required" "${label}: status=${code} expected=401_or_403 url=${url} excerpt='${excerpt}'"
  fi
}

run_host_command() {
  local cmd="$1"
  if [[ "${SMOKE_MODE}" == "remote" ]]; then
    smoke_ssh "bash -lc $(printf '%q' "${cmd}")"
  else
    bash -lc "${cmd}"
  fi
}

run_host_capture() {
  local cmd="$1"
  if [[ "${SMOKE_MODE}" == "remote" ]]; then
    smoke_ssh "bash -lc $(printf '%q' "${cmd}")"
  else
    bash -lc "${cmd}"
  fi
}

check_systemd_service_required() {
  local service_name="$1"

  if ! run_host_command "command -v systemctl >/dev/null 2>&1" >/dev/null 2>&1; then
    fail "required" "service ${service_name}: systemctl unavailable"
    return
  fi

  if run_host_command "systemctl is-active --quiet '${service_name}.service'" >/dev/null 2>&1; then
    pass "required" "service ${service_name}: active"
    return
  fi

  local state_detail
  state_detail="$(run_host_capture "systemctl is-active '${service_name}.service' 2>&1" || true)"

  if [[ "${state_detail}" =~ [Nn]ot[[:space:]]found|[Nn]o[[:space:]]such[[:space:]]file|[Nn]ot[[:space:]]loaded|[Cc]ould[[:space:]]not[[:space:]]be[[:space:]]found ]]; then
    fail "required" "service ${service_name}: missing"
  elif [[ "${state_detail}" =~ [Pp]ermission[[:space:]]denied|[Aa]ccess[[:space:]]denied|[Ii]nteractive[[:space:]]authentication[[:space:]]required ]]; then
    fail "required" "service ${service_name}: no-privilege (${state_detail})"
  elif [[ "${state_detail}" =~ [Nn]ot[[:space:]]been[[:space:]]booted[[:space:]]with[[:space:]]systemd|[Ff]ailed[[:space:]]to[[:space:]]connect[[:space:]]to[[:space:]]bus ]]; then
    fail "required" "service ${service_name}: systemd-unavailable (${state_detail})"
  elif [[ "${state_detail}" =~ ^inactive|^failed|^activating|^deactivating ]]; then
    fail "required" "service ${service_name}: inactive-state=${state_detail}"
  else
    fail "required" "service ${service_name}: inactive-or-unknown (${state_detail:-unknown})"
  fi
}

check_nginx_config_required() {
  if ! run_host_command "command -v nginx >/dev/null 2>&1" >/dev/null 2>&1; then
    fail "required" "nginx config: nginx unavailable"
    return
  fi

  local nginx_output
  nginx_output="$(run_host_capture "nginx -t 2>&1" || true)"

  if [[ "${nginx_output}" =~ [Pp]ermission[[:space:]]denied ]]; then
    fail "required" "nginx config: no-privilege (${nginx_output})"
  elif [[ "${nginx_output}" =~ [Tt]est[[:space:]]is[[:space:]]successful|syntax[[:space:]]is[[:space:]]ok ]]; then
    pass "required" "nginx config: nginx -t passed"
  else
    fail "required" "nginx config: nginx -t failed (${nginx_output})"
  fi
}

check_port_listening_required() {
  local port="$1"

  local socket_output=""
  if run_host_command "command -v ss >/dev/null 2>&1" >/dev/null 2>&1; then
    socket_output="$(run_host_capture "ss -ltn '( sport = :${port} )' 2>&1" || true)"
    if [[ "${socket_output}" =~ [Pp]ermission[[:space:]]denied|[Oo]peration[[:space:]]not[[:space:]]permitted ]]; then
      fail "required" "port ${port}: no-privilege (ss)"
      return
    fi
    if [[ "${socket_output}" =~ LISTEN ]]; then
      pass "required" "port ${port}: listening"
    else
      fail "required" "port ${port}: not-listening"
    fi
    return
  fi

  if run_host_command "command -v netstat >/dev/null 2>&1" >/dev/null 2>&1; then
    socket_output="$(run_host_capture "netstat -ltn 2>&1" || true)"
    if [[ "${socket_output}" =~ [Pp]ermission[[:space:]]denied|[Oo]peration[[:space:]]not[[:space:]]permitted ]]; then
      fail "required" "port ${port}: no-privilege (netstat)"
      return
    fi
    if printf '%s\n' "${socket_output}" | rg -q "[:.]${port}[[:space:]].*LISTEN"; then
      pass "required" "port ${port}: listening"
    else
      fail "required" "port ${port}: not-listening"
    fi
    return
  fi

  fail "required" "port ${port}: ss/netstat unavailable"
}

find_container_by_hint() {
  local hint="$1"
  run_host_capture "docker ps -a --format '{{.ID}}|{{.Names}}|{{.Image}}' 2>&1 | rg -i --max-count 1 --fixed-strings '${hint}'" || true
}

check_container_status_optional() {
  local label="$1"
  local hint="$2"

  if ! run_host_command "command -v docker >/dev/null 2>&1" >/dev/null 2>&1; then
    warn "optional" "container ${label}: docker-cli-unavailable"
    return
  fi

  local docker_listing
  docker_listing="$(find_container_by_hint "${hint}")"

  if [[ "${docker_listing}" =~ [Pp]ermission[[:space:]]denied|[Oo]peration[[:space:]]not[[:space:]]permitted|[Cc]annot[[:space:]]connect[[:space:]]to[[:space:]]the[[:space:]]Docker[[:space:]]daemon|permission[[:space:]]denied[[:space:]]while[[:space:]]trying[[:space:]]to[[:space:]]connect ]]; then
    warn "optional" "container ${label}: docker-access-unavailable (${docker_listing})"
    return
  fi

  if [[ -z "${docker_listing}" ]]; then
    warn "optional" "container ${label}: not-found (hint=${hint})"
    return
  fi

  local container_id="${docker_listing%%|*}"
  local state_health
  state_health="$(run_host_capture "docker inspect --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' '${container_id}' 2>&1" || true)"

  if [[ "${state_health}" =~ [Pp]ermission[[:space:]]denied|[Oo]peration[[:space:]]not[[:space:]]permitted|[Cc]annot[[:space:]]connect[[:space:]]to[[:space:]]the[[:space:]]Docker[[:space:]]daemon ]]; then
    warn "optional" "container ${label}: docker-access-unavailable (${state_health})"
    return
  fi

  local state="${state_health%%|*}"
  local health="${state_health##*|}"

  if [[ "${state}" == "running" && ( "${health}" == "healthy" || "${health}" == "none" ) ]]; then
    pass "optional" "container ${label}: state=${state} health=${health}"
    return
  fi

  warn "optional" "container ${label}: unhealthy state=${state} health=${health}"
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

phase "services"
check_systemd_service_required "${SMOKE_SERVICE_API}"
check_systemd_service_required "${SMOKE_SERVICE_BLAZOR}"
check_systemd_service_required "${SMOKE_SERVICE_ADMINPANEL}"
check_nginx_config_required
check_port_listening_required "${SMOKE_API_PORT}"
check_port_listening_required "${SMOKE_BLAZOR_PORT}"
check_port_listening_required "${SMOKE_ADMINPANEL_PORT}"

phase "http-routes"
check_http_required_host "api health loopback" "${SMOKE_API_LOOPBACK_HEALTH_URL}" '^2[0-9][0-9]$'
check_http_required_host "api health external" "${SMOKE_API_EXTERNAL_HEALTH_URL}" '^2[0-9][0-9]$'
check_surfaces_latest_required "api surfaces latest" "${SMOKE_SURFACES_LATEST_URL}"
check_http_required_host "Blazor home" "${SMOKE_BLAZOR_HOME_URL}" '^2[0-9][0-9]$'
check_http_required_host "admin health loopback" "${SMOKE_ADMIN_LOOPBACK_HEALTH_URL}" '^2[0-9][0-9]$'
check_http_required_host "admin health external" "${SMOKE_ADMIN_EXTERNAL_HEALTH_URL}" '^2[0-9][0-9]$'
check_http_auth_denied_required "admin protected unauthenticated" "${SMOKE_ADMIN_PROTECTED_URL}"

phase "containers"
check_container_status_optional "postgres" "${SMOKE_POSTGRES_CONTAINER_HINT}"
check_container_status_optional "keycloak" "${SMOKE_KEYCLOAK_CONTAINER_HINT}"

phase "summary"
printf "RESULT required_failures=%s optional_warnings=%s\n" "${required_failures}" "${optional_warnings}"

if (( required_failures > 0 )); then
  exit 1
fi

exit 0
