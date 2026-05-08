#!/usr/bin/env bash
# s01-api-production-contract.sh — Deterministic local verifier for S01 API deploy/runtime contract.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly DEPLOY_SCRIPT="${ROOT_DIR}/scripts/deploy-backend.sh"
readonly DEPLOY_CONFIG_SCRIPT="${ROOT_DIR}/scripts/deploy-config.sh"
readonly S05_VERIFY_SCRIPT="${ROOT_DIR}/scripts/verify/s05-local-surfaces.sh"
readonly DEPLOY_DOC="${ROOT_DIR}/docs/DEPLOYMENT.md"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/s01-api-production-contract.sh

Runs deterministic local checks for S01 API production/runtime contract:
  1) bash syntax check for deploy and related verify scripts
  2) static token checks for deploy precheck/health gates and health routes
  3) targeted API endpoint test suite (ApiEndpointTests)
  4) docs check for local verifier command and --no-launch-profile gotcha

Optional:
  S01_ALLOW_REMOTE_URL_CHECKS=1  Enables explicit remote URL shape checks (no network calls).
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

fail() {
  echo "S01_VERIFY_FAIL: $*" >&2
  exit 1
}

require_file() {
  local path="$1"
  [[ -f "${path}" ]] || fail "missing_file path=${path}"
}

check_contains() {
  local file="$1"
  local token="$2"
  if ! grep -Fq -- "${token}" "${file}"; then
    fail "missing_token file=${file} token=${token}"
  fi
}

echo "==> S01 verify: file presence"
require_file "${DEPLOY_SCRIPT}"
require_file "${DEPLOY_CONFIG_SCRIPT}"
require_file "${S05_VERIFY_SCRIPT}"
require_file "${DEPLOY_DOC}"
require_file "${TEST_PROJECT}"

echo "==> S01 verify: bash syntax"
bash -n "${DEPLOY_SCRIPT}"
bash -n "${S05_VERIFY_SCRIPT}"

# Launch profile gotcha: pinning ASPNETCORE_URLS with dotnet run must include --no-launch-profile.
echo "==> S01 verify: launch-profile override guard"
if grep -F "ASPNETCORE_URLS=" "${S05_VERIFY_SCRIPT}" >/dev/null 2>&1; then
  check_contains "${S05_VERIFY_SCRIPT}" "dotnet run --no-launch-profile"
fi

echo "==> S01 verify: deploy contract and health-gate tokens"
# Precheck contract markers
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_PRECHECK_FAIL: missing_env_file"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_PRECHECK_FAIL: missing_env_var ConnectionStrings__HappyGymStats_or_HAPPYGYMSTATS_CONNECTION_STRING"
check_contains "${DEPLOY_SCRIPT}" "ProvisionalToken__SigningKey"
check_contains "${DEPLOY_SCRIPT}" "HAPPYGYMSTATS_SURFACES_CACHE_DIR"
check_contains "${DEPLOY_SCRIPT}" "ASPNETCORE_ENVIRONMENT"
check_contains "${DEPLOY_SCRIPT}" "ASPNETCORE_URLS"

# Health gate categories and route anchors.
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=service_inactive"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=loopback_unreachable"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=loopback_non_2xx"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=external_nginx_502"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=external_non_2xx"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=surfaces_meta_non_2xx"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_HEALTH_FAIL: category=surfaces_latest_non_2xx"

# Route variables are defined centrally in deploy-config.sh and consumed by deploy-backend.sh.
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_LOOPBACK_HEALTH_URL"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_EXTERNAL_HEALTH_URL"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_LOOPBACK_SURFACES_META_URL"
check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_LOOPBACK_SURFACES_LATEST_URL"
check_contains "${DEPLOY_CONFIG_SCRIPT}" "/api/v1/torn/health"
check_contains "${DEPLOY_CONFIG_SCRIPT}" "/api/v1/torn/surfaces/meta"
check_contains "${DEPLOY_CONFIG_SCRIPT}" "/api/v1/torn/surfaces/latest"

echo "==> S01 verify: targeted API endpoint tests"
dotnet test "${TEST_PROJECT}" --filter "FullyQualifiedName~ApiEndpointTests"

echo "==> S01 verify: docs anchors"
check_contains "${DEPLOY_DOC}" "bash scripts/verify/s01-api-production-contract.sh"
check_contains "${DEPLOY_DOC}" "--no-launch-profile"

if [[ "${S01_ALLOW_REMOTE_URL_CHECKS:-0}" == "1" ]]; then
  echo "==> S01 verify: remote URL checks (shape only, no network)"
  check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_EXTERNAL_HEALTH_URL"
  check_contains "${DEPLOY_SCRIPT}" "DEPLOY_API_LOOPBACK_HEALTH_URL"
fi

echo "==> S01 verify passed"
