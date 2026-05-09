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

echo "==> verify: no raw player-id log templates"
bash scripts/verify/no-raw-playerid-log-templates.sh

echo "==> dotnet build"
dotnet build

echo "==> dotnet test"
dotnet test
