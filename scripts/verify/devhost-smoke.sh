#!/usr/bin/env bash
# devhost-smoke.sh — Read-only post-deploy checks for torndev.geromet.com.
#
# SCRIPT_CATEGORY=verify
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Mirrors production-smoke.sh in shape but targets the dev host. Read-only: it
# only queries service state and issues GETs.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEVHOST_PUBLIC_HOST:=torndev.geromet.com}"
: "${DEVHOST_API_PORT:=5147}"
: "${DEVHOST_BLAZOR_PORT:=5282}"
: "${SMOKE_MODE:=remote}"

usage() {
  cat <<EOF
Usage: bash scripts/verify/devhost-smoke.sh

SCRIPT_CATEGORY=verify
SCRIPT_MUTATES_SERVER_STATE=0

Checks the torndev.geromet.com dev host after a deploy:
  services  - happygymstats-api-dev / happygymstats-blazor-dev are active
  loopback  - dev API answers on 127.0.0.1:${DEVHOST_API_PORT}
  routes    - public host answers, and is gated to administrators
  isolation - dev API is not sharing the production database

Environment overrides:
  DEVHOST_PUBLIC_HOST  (default: ${DEVHOST_PUBLIC_HOST})
  DEVHOST_API_PORT     (default: ${DEVHOST_API_PORT})
  DEVHOST_BLAZOR_PORT  (default: ${DEVHOST_BLAZOR_PORT})
  SMOKE_MODE           (default: remote)
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

failures=0
warnings=0

phase() { echo "==> devhost smoke: $1"; }
pass()  { echo "[PASS] $1"; }
fail()  { echo "[FAIL] $1" >&2; failures=$((failures + 1)); }
warn()  { echo "[WARN] $1" >&2; warnings=$((warnings + 1)); }

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
remote_exec() { ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

phase "connection"
if remote_exec "set -euo pipefail; echo REMOTE_OK" >/dev/null 2>&1; then
  pass "SSH connectivity to ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
else
  fail "unable to connect to remote host"
  printf '\nRESULT required_failures=%s optional_warnings=%s\n' "${failures}" "${warnings}"
  exit 1
fi

phase "services"
for unit in happygymstats-api-dev happygymstats-blazor-dev; do
  if remote_exec "set -euo pipefail; systemctl is-active --quiet '${unit}'" >/dev/null 2>&1; then
    pass "service active: ${unit}"
  else
    fail "service not active: ${unit}"
  fi
done

phase "loopback"
if remote_exec "set -euo pipefail; curl -fsS -o /dev/null 'http://127.0.0.1:${DEVHOST_API_PORT}/api/v1/torn/health'" >/dev/null 2>&1; then
  pass "dev API health responds on 127.0.0.1:${DEVHOST_API_PORT}"
else
  fail "dev API health did not respond on 127.0.0.1:${DEVHOST_API_PORT}"
fi

phase "isolation"
# A dev API pointed at the production database is the failure this whole host
# design exists to prevent, so check it explicitly rather than trusting the unit.
dev_conn="$(remote_exec "set -euo pipefail; sudo -n grep -h '^ConnectionStrings__HappyGymStats' /etc/happygymstats/api-dev.env 2>/dev/null || true" 2>/dev/null || true)"
prod_conn="$(remote_exec "set -euo pipefail; sudo -n grep -h '^ConnectionStrings__HappyGymStats' /etc/happygymstats/api.env 2>/dev/null || true" 2>/dev/null || true)"
if [[ -z "${dev_conn}" ]]; then
  warn "could not read dev connection string (needs passwordless sudo); verify database isolation by hand"
elif [[ "${dev_conn}" == *REPLACE_ME* ]]; then
  fail "/etc/happygymstats/api-dev.env still contains REPLACE_ME placeholders"
elif [[ -n "${prod_conn}" && "${dev_conn}" == "${prod_conn}" ]]; then
  fail "dev and production API share a connection string — dev migrations would alter production"
else
  pass "dev API connection string differs from production"
fi

phase "http-routes"
public_base="https://${DEVHOST_PUBLIC_HOST}"
status="$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 "${public_base}/" || echo "000")"
case "${status}" in
  000)
    fail "no HTTP response from ${public_base}/ (DNS A record present? nginx reloaded?)"
    ;;
  200)
    # The gate challenges anonymous visitors, so a bare 200 on the root means it
    # is serving the app to the public.
    fail "${public_base}/ returned 200 to an anonymous request — admin-only gate is not active"
    ;;
  302|303|307)
    pass "${public_base}/ redirects anonymous visitors to sign-in (${status})"
    ;;
  403)
    pass "${public_base}/ denies anonymous visitors (403)"
    ;;
  *)
    warn "${public_base}/ returned unexpected status ${status}"
    ;;
esac

phase "summary"
printf 'RESULT required_failures=%s optional_warnings=%s\n' "${failures}" "${warnings}"

if (( failures > 0 )); then
  exit 1
fi

exit 0
