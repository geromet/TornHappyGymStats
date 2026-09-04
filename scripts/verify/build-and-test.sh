#!/usr/bin/env bash
# build-and-test.sh — Build solution and run full test suite.
set -euo pipefail

usage() {
  cat <<EOF
Usage: bash scripts/verify/build-and-test.sh

Runs:
  1) dotnet build
  2) dotnet test
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

# PREFLIGHT BEFORE ANY VERIFIER CAN PRINT PASS.
#
# The gate transitively needs these. ripgrep is the one that actually bites: it
# is absent from the GitHub runner, and its absence is what let the raw-player-id
# check report PASS in CI without opening a file. The rest are present on both a
# workstation and the runner, so preflighting them is cheap insurance rather than
# a live fix — but a missing tool must be its own loud failure, never a pass.
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
echo "==> preflight: tools the gate depends on"
verify_require_commands rg dotnet grep sed awk wc
echo "PASS: rg, dotnet, grep, sed, awk, wc all present"

echo "==> verify: no raw player-id log templates"
bash scripts/verify/no-raw-playerid-log-templates.sh

echo "==> verify: ranked-war scouting contract (w05)"
bash scripts/verify/w05-scouting-contract.sh

echo "==> verify: honest signal on the war board (U001)"
bash scripts/verify/u001-honest-signal.sh

echo "==> verify: operator console covers every script"
bash scripts/verify/menu-contract.sh

echo "==> verify: chain command contract (w06)"
bash scripts/verify/w06-chain-contract.sh
bash scripts/verify/w07-key-vault-contract.sh

echo "==> dotnet build"
dotnet build

echo "==> dotnet test"
dotnet test
