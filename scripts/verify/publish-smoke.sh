#!/usr/bin/env bash
# publish-smoke.sh — Smoke-test API publish path for linux-x64.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"
readonly SMOKE_DIR="/tmp/hgs-smoke"

usage() {
  cat <<EOF
Usage: bash scripts/verify/publish-smoke.sh

Builds and tests the solution, then publishes HappyGymStats.Api for linux-x64
to a temp dir, verifies the binary exists, and cleans up.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

echo "==> Building and running tests"
dotnet test "${ROOT_DIR}/HappyGymStats.sln"

echo "==> Smoke-test publish (linux-x64, self-contained single-file)"
rm -rf "${SMOKE_DIR}"
dotnet publish "${PROJECT}" \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -c Release \
  -o "${SMOKE_DIR}"

echo "    Published to ${SMOKE_DIR}"
ls -lh "${SMOKE_DIR}"

echo "==> Cleaning up smoke-test output"
rm -rf "${SMOKE_DIR}"

echo "==> Publish smoke test passed."
