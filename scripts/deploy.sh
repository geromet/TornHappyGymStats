#!/usr/bin/env bash
# deploy.sh — Deploy backend API and frontend to host.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

usage() {
  cat <<EOF
Usage: bash scripts/deploy.sh [--target backend|frontend|all]

Targets:
  backend   Deploy API only
  frontend  Deploy web frontend only
  all       Deploy backend then frontend (default)

Post-deploy smoke:
  DEPLOY_RUN_SMOKE=1      Run scripts/verify/production-smoke.sh after target deploy finishes.
  DEPLOY_SMOKE_MODE=remote  Smoke mode override (default: remote)
EOF
}

TARGET="all"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target) TARGET="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

case "$TARGET" in
  backend) bash "${SCRIPT_DIR}/deploy-backend.sh" ;;
  frontend) bash "${SCRIPT_DIR}/deploy-frontend.sh" ;;
  all)
    bash "${SCRIPT_DIR}/deploy-backend.sh"
    bash "${SCRIPT_DIR}/deploy-frontend.sh"
    ;;
  *) echo "Invalid --target: ${TARGET}" >&2; exit 1 ;;
esac

deploy_run_post_deploy_smoke_if_enabled

echo "==> Deploy target '${TARGET}' complete"
