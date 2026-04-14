#!/usr/bin/env bash
# verify-e2e.sh — Smoke-test: publish a single RID and clean up.
# Exercises the same publish path as publish-all.sh but only for the host RID.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT="${ROOT_DIR}/src/HappyGymStats/HappyGymStats.csproj"
readonly SMOKE_DIR="/tmp/hgs-smoke"

echo "==> Building and running unit tests"
dotnet test --project "${PROJECT}"

echo "==> Smoke-test publish (linux-x64, self-contained single-file)"
rm -rf "${SMOKE_DIR}"
dotnet publish "${PROJECT}" \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -c Release \
  -o "${SMOKE_DIR}"

echo "    Published to ${SMOKE_DIR}"
ls -lh "${SMOKE_DIR}/HappyGymStats"

echo "==> Cleaning up smoke-test output"
rm -rf "${SMOKE_DIR}"

echo "==> E2E smoke test passed."
