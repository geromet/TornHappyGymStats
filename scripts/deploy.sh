#!/usr/bin/env bash
# deploy.sh — Deploy both API and Blazor frontend.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: bash scripts/deploy.sh"
  echo "Deploys backend (API) then frontend (Blazor) in sequence."
  exit 0
fi

bash "${SCRIPT_DIR}/deploy-backend.sh"
bash "${SCRIPT_DIR}/deploy-frontend.sh"
