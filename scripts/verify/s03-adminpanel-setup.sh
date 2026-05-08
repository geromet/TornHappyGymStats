#!/usr/bin/env bash
set -euo pipefail

<<<<<<< Updated upstream
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
=======
readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly SETUP_SCRIPT="${ROOT_DIR}/scripts/setup-adminpanel-server.sh"
readonly SUDOERS_FILE="${ROOT_DIR}/infra/sudoers-happygymstats"

failures=0

pass() { printf 'PASS %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1"; failures=$((failures + 1)); }

require_token() {
  local file="$1" token="$2" label="$3"
  if rg -F -q "$token" "$file"; then
    pass "$label"
  else
    fail "$label (missing token: $token)"
  fi
}

[[ -f "${SETUP_SCRIPT}" ]] && pass "setup script exists" || fail "missing setup script"
[[ -f "${SUDOERS_FILE}" ]] && pass "sudoers file exists" || fail "missing sudoers file"

if bash -n "${SETUP_SCRIPT}"; then
  pass "setup script bash -n"
else
  fail "setup script bash -n"
fi

if rg -n -- "--dry-run" "${SETUP_SCRIPT}" >/dev/null; then
  pass "dry-run flag supported"
else
  fail "dry-run flag supported"
fi

if rg -n -- "--execute" "${SETUP_SCRIPT}" >/dev/null && rg -n -- "--confirm-remote-setup" "${SETUP_SCRIPT}" >/dev/null; then
  pass "mutating path explicitly gated"
else
  fail "mutating path explicitly gated"
fi

require_token "${SETUP_SCRIPT}" "SCRIPT_AUTOMATION_SAFE_DEFAULT=1" "automation-safe token present"

if rg -n "NOPASSWD: (/usr/bin/|/bin/)?(install|chown|chmod|rm|ln|rsync|find)$|NOPASSWD: ALL|/bin/bash|/usr/bin/bash|sh -c" "${SUDOERS_FILE}" >/dev/null; then
  fail "sudoers contains forbidden broad grants"
else
  pass "sudoers excludes forbidden broad grants"
fi

printf 'RESULT failures=%s\n' "${failures}"
(( failures == 0 ))
>>>>>>> Stashed changes
