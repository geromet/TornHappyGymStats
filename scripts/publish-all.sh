#!/usr/bin/env bash
# publish-all.sh — Build self-contained single-file binaries for all 6 main RIDs.
# Outputs to dist/<rid>/ in the project root.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT="${ROOT_DIR}/src/HappyGymStats/HappyGymStats.csproj"
readonly RIDS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)

echo "==> Publishing self-contained single-file binaries"
echo "    Project: ${PROJECT}"
echo ""

for rid in "${RIDS[@]}"; do
  echo "==> [${rid}] dotnet publish …"
  dotnet publish "${PROJECT}" \
    -r "${rid}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -c Release \
    -o "${ROOT_DIR}/dist/${rid}" \
    /warnaserror
  echo "    ✓ dist/${rid}/"
  echo ""
done

echo "==> All RIDs published successfully."
