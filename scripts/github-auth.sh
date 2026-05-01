#!/usr/bin/env bash
# github-auth.sh — deterministic GitHub auth setup for this repo.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<EOF
Usage:
  bash scripts/github-auth.sh setup    # load .env GITHUB_TOKEN, auth gh, wire git credentials
  bash scripts/github-auth.sh status   # show gh + git credential status
  bash scripts/github-auth.sh logout   # clear gh auth state

Notes:
- Expects GITHUB_TOKEN in .env (repo root).
- Never prints token values.
EOF
}

cmd="${1:-status}"

load_env_token() {
  if [[ ! -f "${ROOT_DIR}/.env" ]]; then
    echo ".env not found at ${ROOT_DIR}/.env" >&2
    exit 1
  fi

  set -a
  # shellcheck disable=SC1090
  source "${ROOT_DIR}/.env"
  set +a

  if [[ -z "${GITHUB_TOKEN:-}" ]]; then
    echo "GITHUB_TOKEN is missing in .env" >&2
    exit 1
  fi
}

case "$cmd" in
  setup)
    load_env_token
    gh auth status -h github.com >/dev/null 2>&1 || true
    gh auth setup-git
    gh auth status -h github.com
    git config --global --get-all credential.https://github.com.helper || true
    ;;
  status)
    gh auth status -h github.com || true
    echo "--- git credential helper for github.com ---"
    git config --global --get-all credential.https://github.com.helper || true
    ;;
  logout)
    gh auth logout -h github.com -u geromet || true
    echo "Logged out (if session existed)."
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    echo "Unknown command: $cmd" >&2
    usage
    exit 1
    ;;
esac
