#!/usr/bin/env bash
set -euo pipefail

# S03 gate: AdminPanel server setup contract.
# Resolved 2026-09 from the prior merge-conflict state: the sudoers-based
# bootstrap was abandoned (deploys use manual sudo authing); this gate now
# pins the nginx-bootstrap script as it exists, the systemd loopback binding,
# and the public admin health route. The sudoers file must stay gone, both on
# disk and in the git index.

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SETUP_SCRIPT="${ROOT_DIR}/scripts/setup-adminpanel-server.sh"
NGINX_CONF="${ROOT_DIR}/infra/nginx-adminpanel.conf"
SERVICE_FILE="${ROOT_DIR}/infra/happygymstats-adminpanel.service"
HEALTH_CONTROLLER="${ROOT_DIR}/src/HappyGymStats.AdminPanel/Controllers/AdminHealthController.cs"
SUDOERS_FILE="${ROOT_DIR}/infra/sudoers-happygymstats"

assert_contains() {
  local file="$1"
  local needle="$2"
  if ! grep -Fq -- "$needle" "$file"; then
    echo "S03_VERIFY_FAIL: missing_token file=${file} token=${needle}" >&2
    exit 1
  fi
}

assert_not_contains() {
  local file="$1"
  local needle="$2"
  if grep -Fq -- "$needle" "$file"; then
    echo "S03_VERIFY_FAIL: unexpected_token file=${file} token=${needle}" >&2
    exit 1
  fi
}

echo "S03_VERIFY: bash syntax check"
bash -n "$SETUP_SCRIPT"

echo "S03_VERIFY: setup script safety gating checks"
assert_contains "$SETUP_SCRIPT" "SCRIPT_AUTOMATION_SAFE_DEFAULT=1"
assert_contains "$SETUP_SCRIPT" "--execute"
assert_contains "$SETUP_SCRIPT" "--confirm-remote-setup"
assert_contains "$SETUP_SCRIPT" "only when both flags are present"
assert_contains "$SETUP_SCRIPT" "nginx-adminpanel.conf"
assert_contains "$SETUP_SCRIPT" "\${SUDO_CMD} systemctl reload nginx"
assert_contains "$SETUP_SCRIPT" "\${SUDO_CMD} nginx -t"

echo "S03_VERIFY: systemd unit loopback binding checks"
assert_contains "$SERVICE_FILE" "Environment=ASPNETCORE_URLS=http://127.0.0.1:5048"
assert_contains "$SERVICE_FILE" "ExecStart=/var/www/happygymstats-adminpanel/current/HappyGymStats.AdminPanel"

echo "S03_VERIFY: admin health route checks"
assert_contains "$HEALTH_CONTROLLER" "[Route(\"admin/health\")]"
assert_contains "$HEALTH_CONTROLLER" "public IActionResult Get() => Ok"

echo "S03_VERIFY: nginx config sanity"
assert_contains "$NGINX_CONF" "127.0.0.1:5048"

echo "S03_VERIFY: sudoers bootstrap stays removed"
if [[ -f "${SUDOERS_FILE}" ]]; then
  echo "S03_VERIFY_FAIL: sudoers file unexpectedly present: ${SUDOERS_FILE}" >&2
  exit 1
fi
assert_not_contains "$SETUP_SCRIPT" "sudoers"

echo "S03_VERIFY: sudoers file untracked by git"
if git -C "$ROOT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if git -C "$ROOT_DIR" ls-files --error-unmatch -- infra/sudoers-happygymstats >/dev/null 2>&1; then
    echo "S03_VERIFY_FAIL: sudoers file still tracked by git: infra/sudoers-happygymstats" >&2
    exit 1
  fi
  echo "S03_VERIFY: confirmed untracked by git (ls-files --error-unmatch: no match)"
else
  echo "S03_VERIFY: not a git worktree, skipping git tracking check"
fi

echo "S03_VERIFY_PASS: setup verifier checks passed"
