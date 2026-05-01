#!/usr/bin/env bash
# run-cli.sh — Build and run the HappyGymStats CLI with repo-local data paths.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly SOLUTION="${ROOT_DIR}/HappyGymStats.sln"
readonly PROJECT="${ROOT_DIR}/src/HappyGymStats.Cli/HappyGymStats.Cli.csproj"

usage() {
  cat <<EOF
Usage: bash scripts/run-cli.sh [-- <cli args...>]

Builds the solution and starts the CLI project.
Sets HAPPYGYMSTATS_DATA_DIR to repository data/ so runs are stable across machines.

Examples:
  bash scripts/run-cli.sh
  bash scripts/run-cli.sh -- fetch
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

export HAPPYGYMSTATS_DATA_DIR="${ROOT_DIR}/data"
cd "${ROOT_DIR}"

echo "==> Building HappyGymStats"
dotnet build "${SOLUTION}"

echo "==> Starting HappyGymStats CLI"
echo "    Data directory: ${HAPPYGYMSTATS_DATA_DIR}"
exec dotnet run --no-build --project "${PROJECT}" -- "$@"
