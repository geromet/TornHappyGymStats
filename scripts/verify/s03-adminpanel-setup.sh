#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SETUP_SCRIPT="${ROOT_DIR}/scripts/setup-adminpanel-server.sh"
SERVICE_FILE="${ROOT_DIR}/infra/happygymstats-adminpanel.service"
HEALTH_CONTROLLER="${ROOT_DIR}/src/HappyGymStats.AdminPanel/Controllers/AdminHealthController.cs"

assert_contains() {
  local file="$1"
  local needle="$2"
  if ! grep -Fq "$needle" "$file"; then
    echo "S03_VERIFY_FAIL: missing_token file=${file} token=${needle}" >&2
    exit 1
  fi
}

echo "S03_VERIFY: bash syntax check"
bash -n "$SETUP_SCRIPT"

echo "S03_VERIFY: setup script static checks"
assert_contains "$SETUP_SCRIPT" "verify-service-active"
assert_contains "$SETUP_SCRIPT" "service-inactive:"
assert_contains "$SETUP_SCRIPT" "port-unavailable"
assert_contains "$SETUP_SCRIPT" "http-non-2xx"
assert_contains "$SETUP_SCRIPT" "HEALTH_URL=\"http://127.0.0.1:5048/admin/health\""
assert_contains "$SETUP_SCRIPT" "systemctl is-active --quiet '\${SERVICE_NAME}'"
assert_contains "$SETUP_SCRIPT" "systemctl status '\${SERVICE_NAME}' --no-pager"

echo "S03_VERIFY: systemd unit loopback binding checks"
assert_contains "$SERVICE_FILE" "Environment=ASPNETCORE_URLS=http://127.0.0.1:5048"
assert_contains "$SERVICE_FILE" "ExecStart=/var/www/happygymstats-adminpanel/current/HappyGymStats.AdminPanel"

echo "S03_VERIFY: admin health route checks"
assert_contains "$HEALTH_CONTROLLER" "[Route(\"admin/health\")]"
assert_contains "$HEALTH_CONTROLLER" "public IActionResult Get() => Ok"

echo "S03_VERIFY_PASS: setup verifier checks passed"
