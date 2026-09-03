#!/usr/bin/env bash
# recon-devhost-fetch.sh — Backwards-compatible alias for the generic wrapper.
#
# Superseded by scripts/recon-fetch.sh, which runs any collector and adds --sudo.
# Kept so existing muscle memory and docs keep working.
set -euo pipefail
readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo "==> scripts/recon-devhost-fetch.sh is now an alias for:"
echo "    bash scripts/recon-fetch.sh devhost $*"
echo
exec bash "${SCRIPT_DIR}/recon-fetch.sh" devhost "$@"
