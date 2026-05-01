#!/usr/bin/env bash
# deploy.sh — Deploy backend API and frontend to host.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

usage() {
  cat <<EOF
Usage: bash scripts/deploy.sh [--target backend|frontend|all]

Targets:
  backend   Deploy API only
  frontend  Deploy web frontend only
  all       Deploy backend then frontend (default)
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
