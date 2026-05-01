#!/usr/bin/env bash
# deploy-frontend.sh — Frontend deployment helper.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly WEB_DIR="${ROOT_DIR}/web"

MODE="validate"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-frontend.sh [--mode validate|trigger]

Modes:
  validate  Validate frontend files used by GitHub Pages (default)
  trigger   Trigger Pages workflow via gh CLI (manual dispatch)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) MODE="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -d "${WEB_DIR}" ]]; then
  echo "web/ directory not found at ${WEB_DIR}" >&2
  exit 1
fi

case "${MODE}" in
  validate)
    echo "==> Validating frontend deploy inputs"
    test -f "${WEB_DIR}/index.html"
    test -f "${WEB_DIR}/app.js"
    test -f "${WEB_DIR}/styles.css"
    echo "==> Frontend deploy inputs look valid"
    echo "GitHub Pages deploy happens automatically on push to main."
    ;;
  trigger)
    command -v gh >/dev/null 2>&1 || { echo "gh CLI is required for --mode trigger" >&2; exit 1; }
    echo "==> Triggering GitHub Pages workflow"
    gh workflow run pages.yml
    echo "==> Triggered. Check status with: gh run list --workflow pages.yml"
    ;;
  *)
    echo "Invalid mode: ${MODE}. Use validate|trigger." >&2
    exit 1
    ;;
esac
