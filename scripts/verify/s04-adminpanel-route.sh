#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_NAME="$(basename "$0")"

: "${ADMINPANEL_LOOPBACK_HEALTH_URL:=http://127.0.0.1:5048/admin/health}"
: "${ADMINPANEL_EXTERNAL_HEALTH_URL:=https://admin.geromet.com/admin/health}"
: "${ADMINPANEL_PROTECTED_URL:=https://admin.geromet.com/admin/api/v1/import-runs}"
: "${ADMINPANEL_ROUTE_LOCAL_ONLY:=0}"
: "${ADMINPANEL_CURL_TIMEOUT_SECONDS:=8}"

usage() {
  cat <<EOF
Usage: bash scripts/verify/s04-adminpanel-route.sh

Environment overrides:
  ADMINPANEL_LOOPBACK_HEALTH_URL   Loopback health endpoint (default: ${ADMINPANEL_LOOPBACK_HEALTH_URL})
  ADMINPANEL_EXTERNAL_HEALTH_URL   External health endpoint via nginx host (default: ${ADMINPANEL_EXTERNAL_HEALTH_URL})
  ADMINPANEL_PROTECTED_URL         Protected admin API endpoint without auth (default: ${ADMINPANEL_PROTECTED_URL})
  ADMINPANEL_ROUTE_LOCAL_ONLY      1 = run loopback check only (skip external/auth checks)
  ADMINPANEL_CURL_TIMEOUT_SECONDS  Curl timeout in seconds (default: ${ADMINPANEL_CURL_TIMEOUT_SECONDS})

Notes:
- This script performs read-only GET requests only.
- No auth headers/tokens are sent or printed.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

run_check() {
  local name="$1"
  local url="$2"

  set +e
  local result
  result="$(curl -sS -o /dev/null -w "%{http_code} %{exitcode}" --max-time "${ADMINPANEL_CURL_TIMEOUT_SECONDS}" "$url" 2>/dev/null)"
  local rc=$?
  set -e

  local http_code="000"
  local curl_exit="1"

  if [[ -n "${result}" ]]; then
    http_code="${result%% *}"
    curl_exit="${result##* }"
  fi

  if [[ ${rc} -ne 0 && "${curl_exit}" == "0" ]]; then
    curl_exit="${rc}"
  fi

  printf "%s|%s|%s\n" "${name}" "${http_code}" "${curl_exit}"
}

is_2xx() {
  local code="$1"
  [[ "$code" =~ ^2[0-9][0-9]$ ]]
}

is_auth_denied() {
  local code="$1"
  [[ "$code" == "401" || "$code" == "403" ]]
}

is_upstream_failure() {
  local code="$1"
  [[ "$code" == "502" || "$code" == "503" || "$code" == "504" ]]
}

report_line() {
  local marker="$1"
  local msg="$2"
  printf "%s %s\n" "$marker" "$msg"
}

failures=0

report_line "ROUTE_CHECK_START" "script=${SCRIPT_NAME} local_only=${ADMINPANEL_ROUTE_LOCAL_ONLY}"

loopback_out="$(run_check "loopback_health" "${ADMINPANEL_LOOPBACK_HEALTH_URL}")"
loopback_code="$(echo "${loopback_out}" | cut -d'|' -f2)"
loopback_curl_exit="$(echo "${loopback_out}" | cut -d'|' -f3)"

if [[ "${loopback_curl_exit}" != "0" ]]; then
  report_line "ROUTE_HEALTH_LOOPBACK_FAIL" "curl_exit=${loopback_curl_exit} status=${loopback_code} url=${ADMINPANEL_LOOPBACK_HEALTH_URL}"
  failures=$((failures + 1))
elif is_2xx "${loopback_code}"; then
  report_line "ROUTE_HEALTH_LOOPBACK_PASS" "status=${loopback_code} url=${ADMINPANEL_LOOPBACK_HEALTH_URL}"
else
  report_line "ROUTE_HEALTH_LOOPBACK_FAIL" "status=${loopback_code} expected=2xx url=${ADMINPANEL_LOOPBACK_HEALTH_URL}"
  failures=$((failures + 1))
fi

if [[ "${ADMINPANEL_ROUTE_LOCAL_ONLY}" == "1" ]]; then
  report_line "ROUTE_EXTERNAL_SKIPPED" "reason=local_only"
  report_line "ROUTE_CHECK_DONE" "result=$( [[ ${failures} -eq 0 ]] && echo pass || echo fail )"
  exit ${failures}
fi

external_out="$(run_check "external_health" "${ADMINPANEL_EXTERNAL_HEALTH_URL}")"
external_code="$(echo "${external_out}" | cut -d'|' -f2)"
external_curl_exit="$(echo "${external_out}" | cut -d'|' -f3)"

if [[ "${external_curl_exit}" != "0" ]]; then
  report_line "ROUTE_EXTERNAL_UNREACHABLE" "curl_exit=${external_curl_exit} status=${external_code} url=${ADMINPANEL_EXTERNAL_HEALTH_URL}"
  failures=$((failures + 1))
elif is_upstream_failure "${external_code}"; then
  report_line "ROUTE_EXTERNAL_UPSTREAM_FAIL" "status=${external_code} url=${ADMINPANEL_EXTERNAL_HEALTH_URL}"
  failures=$((failures + 1))
elif is_2xx "${external_code}"; then
  report_line "ROUTE_EXTERNAL_HEALTH_PASS" "status=${external_code} url=${ADMINPANEL_EXTERNAL_HEALTH_URL}"
else
  report_line "ROUTE_EXTERNAL_HEALTH_FAIL" "status=${external_code} expected=2xx url=${ADMINPANEL_EXTERNAL_HEALTH_URL}"
  failures=$((failures + 1))
fi

protected_out="$(run_check "protected_without_auth" "${ADMINPANEL_PROTECTED_URL}")"
protected_code="$(echo "${protected_out}" | cut -d'|' -f2)"
protected_curl_exit="$(echo "${protected_out}" | cut -d'|' -f3)"

if [[ "${protected_curl_exit}" != "0" ]]; then
  report_line "ROUTE_PROTECTED_UNREACHABLE" "curl_exit=${protected_curl_exit} status=${protected_code} url=${ADMINPANEL_PROTECTED_URL}"
  failures=$((failures + 1))
elif is_upstream_failure "${protected_code}"; then
  report_line "ROUTE_PROTECTED_UPSTREAM_FAIL" "status=${protected_code} url=${ADMINPANEL_PROTECTED_URL}"
  failures=$((failures + 1))
elif is_auth_denied "${protected_code}"; then
  report_line "ROUTE_PROTECTED_AUTH_PASS" "status=${protected_code} url=${ADMINPANEL_PROTECTED_URL}"
else
  report_line "ROUTE_PROTECTED_AUTH_FAIL" "status=${protected_code} expected=401_or_403 url=${ADMINPANEL_PROTECTED_URL}"
  failures=$((failures + 1))
fi

report_line "ROUTE_CHECK_DONE" "result=$( [[ ${failures} -eq 0 ]] && echo pass || echo fail )"

exit ${failures}
