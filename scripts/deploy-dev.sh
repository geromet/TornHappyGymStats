#!/usr/bin/env bash
# deploy-dev.sh — Deploy API + Blazor to the torndev.geromet.com dev host.
#
# Deliberately a thin wrapper: deploy-backend.sh and deploy-frontend.sh are
# already parameterised by DEPLOY_REMOTE_ROOT / DEPLOY_REMOTE_SERVICE and the
# DEPLOY_BLAZOR_* equivalents, so the dev host is a different set of values, not
# a different set of scripts. Forking them would guarantee drift.
#
# The build published here is byte-identical to production. Everything that
# differs between the two environments lives in the systemd units and
# /etc/happygymstats/api-dev.env on the host.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"

usage() {
  cat <<'EOF'
Usage: bash scripts/deploy-dev.sh [--target backend|frontend|all]

SCRIPT_CATEGORY=deploy
SCRIPT_MUTATES_SERVER_STATE=1

Targets:
  backend   Deploy API only        (-> /var/www/happygymstats-dev, happygymstats-api-dev)
  frontend  Deploy Blazor only     (-> /var/www/happygymstats-blazor-dev, happygymstats-blazor-dev)
  all       Backend then frontend  (default)

Preconditions:
  - bash scripts/setup-devhost-server.sh has been run with --execute
  - /etc/happygymstats/api-dev.env has real values (no REPLACE_ME left)
  - Keycloak client 'happygymstats-web-dev' exists

Overrides (defaults shown):
  DEPLOY_DEV_API_ROOT=/var/www/happygymstats-dev
  DEPLOY_DEV_API_SERVICE=happygymstats-api-dev
  DEPLOY_DEV_BLAZOR_ROOT=/var/www/happygymstats-blazor-dev
  DEPLOY_DEV_BLAZOR_SERVICE=happygymstats-blazor-dev

Connection settings are shared with the production deploy path; see
scripts/deploy-config.sh.
EOF
}

TARGET="all"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --target) TARGET="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

: "${DEPLOY_DEV_API_ROOT:=/var/www/happygymstats-dev}"
: "${DEPLOY_DEV_API_SERVICE:=happygymstats-api-dev}"
: "${DEPLOY_DEV_BLAZOR_ROOT:=/var/www/happygymstats-blazor-dev}"
: "${DEPLOY_DEV_BLAZOR_SERVICE:=happygymstats-blazor-dev}"

# Guardrail: refuse to run if the dev target has been pointed at a production
# root or unit. Cheap to check, and the failure it prevents is deploying an
# unreviewed build over production.
for pair in \
  "DEPLOY_DEV_API_ROOT=${DEPLOY_DEV_API_ROOT}:/var/www/happygymstats" \
  "DEPLOY_DEV_BLAZOR_ROOT=${DEPLOY_DEV_BLAZOR_ROOT}:/var/www/happygymstats-blazor" \
  "DEPLOY_DEV_API_SERVICE=${DEPLOY_DEV_API_SERVICE}:happygymstats-api" \
  "DEPLOY_DEV_BLAZOR_SERVICE=${DEPLOY_DEV_BLAZOR_SERVICE}:happygymstats-blazor"
do
  actual="${pair%:*}"
  forbidden="${pair##*:}"
  if [[ "${actual#*=}" == "${forbidden}" ]]; then
    echo "DEPLOY_DEV_FAIL category=production_target_refused detail=${actual%%=*}=${forbidden}" >&2
    echo "    scripts/deploy-dev.sh must never target production. Use scripts/deploy.sh for that." >&2
    exit 1
  fi
done

echo "==> Dev deploy target=${TARGET}"
echo "    api:    ${DEPLOY_DEV_API_ROOT} (${DEPLOY_DEV_API_SERVICE})"
echo "    blazor: ${DEPLOY_DEV_BLAZOR_ROOT} (${DEPLOY_DEV_BLAZOR_SERVICE})"

deploy_backend_dev() {
  DEPLOY_FORBID_PRODUCTION_TARGET=1 \
  DEPLOY_REMOTE_ROOT="${DEPLOY_DEV_API_ROOT}" \
  DEPLOY_REMOTE_SERVICE="${DEPLOY_DEV_API_SERVICE}" \
    bash "${SCRIPT_DIR}/deploy-backend.sh"
}

deploy_frontend_dev() {
  DEPLOY_FORBID_PRODUCTION_TARGET=1 \
  DEPLOY_BLAZOR_REMOTE_ROOT="${DEPLOY_DEV_BLAZOR_ROOT}" \
  DEPLOY_BLAZOR_REMOTE_SERVICE="${DEPLOY_DEV_BLAZOR_SERVICE}" \
    bash "${SCRIPT_DIR}/deploy-frontend.sh"
}

case "${TARGET}" in
  backend) deploy_backend_dev ;;
  frontend) deploy_frontend_dev ;;
  all)
    deploy_backend_dev
    deploy_frontend_dev
    ;;
  *) echo "Invalid --target: ${TARGET}" >&2; usage; exit 1 ;;
esac

cat <<EOF
==> Dev deploy target '${TARGET}' complete
    Next step: bash scripts/verify/devhost-smoke.sh
EOF
