#!/usr/bin/env bash
# Build HappyGymStats and start the CLI with repository-local data output.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="${SCRIPT_DIR}"
readonly SOLUTION="${ROOT_DIR}/HappyGymStats.sln"
readonly PROJECT="${ROOT_DIR}/src/HappyGymStats/HappyGymStats.csproj"

# Force script-launched runs to use the project-root data directory instead of
# the build-output directory selected by AppContext.BaseDirectory.
export HAPPYGYMSTATS_DATA_DIR="${ROOT_DIR}/data"

cd "${ROOT_DIR}"

echo "==> Building HappyGymStats"
dotnet build "${SOLUTION}"

echo "==> Starting HappyGymStats CLI"
echo "    Data directory: ${HAPPYGYMSTATS_DATA_DIR}"
exec dotnet run --no-build --project "${PROJECT}" -- "$@"
