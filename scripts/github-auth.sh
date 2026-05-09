#!/usr/bin/env bash
# github-auth.sh — deterministic GitHub auth setup for this repo.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<EOF
Usage:
  bash scripts/github-auth.sh setup    # best-effort gh auth setup (non-fatal if token invalid)
  bash scripts/github-auth.sh status   # show gh + git credential status
  bash scripts/github-auth.sh logout   # clear gh auth state

Notes:
- If .env contains GITHUB_TOKEN, setup exports it as GH_TOKEN for gh.
- setup is intentionally non-fatal on invalid/missing token so git flows can continue.
- Never prints token values.
EOF
}

cmd="${1:-status}"

load_env_token_optional() {
  if [[ ! -f "${ROOT_DIR}/.env" ]]; then
    return 0
  fi

  set -a
  # shellcheck disable=SC1090
  source "${ROOT_DIR}/.env"
  set +a

  if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    export GH_TOKEN="${GITHUB_TOKEN}"
  fi
}

warn() {
  echo "WARN: $*" >&2
}

require_gh() {
  if ! command -v gh >/dev/null 2>&1; then
    echo "gh CLI is not installed." >&2
    exit 1
  fi
}

case "$cmd" in
  setup)
    require_gh
    load_env_token_optional

    echo "==> gh auth setup (best effort)"

    if gh auth status -h github.com >/dev/null 2>&1; then
      echo "gh auth status: already authenticated"
    else
      if [[ -n "${GH_TOKEN:-}" ]]; then
        if ! printf '%s' "${GH_TOKEN}" | gh auth login --hostname github.com --with-token >/dev/null 2>&1; then
          warn "GH_TOKEN/GITHUB_TOKEN appears invalid for gh; continuing with git-only auth path."
        fi
      else
        warn "No GITHUB_TOKEN found in .env; skipping gh token login."
      fi
    fi

    if ! gh auth setup-git >/dev/null 2>&1; then
      warn "gh auth setup-git failed; git push may still work via existing credential helper."
    fi

    gh auth status -h github.com || true
    echo "--- git credential helper for github.com ---"
    git config --global --get-all credential.https://github.com.helper || true
    ;;
  status)
    require_gh
    gh auth status -h github.com || true
    echo "--- git credential helper for github.com ---"
    git config --global --get-all credential.https://github.com.helper || true
    ;;
  logout)
    require_gh
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
