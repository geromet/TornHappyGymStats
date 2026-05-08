#!/usr/bin/env bash
# s02-blazor-api-boundary.sh — deterministic verifier for Blazor API boundary/classification tests.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/s02-blazor-api-boundary.sh

Runs deterministic checks for S02 Blazor API boundary + failure classification:
  1) File presence checks for service + tests
  2) dotnet build on test project
  3) targeted BlazorApiFailureTests suite
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

fail() {
  echo "S02_VERIFY_FAIL: $*" >&2
  exit 1
}

require_file() {
  local path="$1"
  [[ -f "${path}" ]] || fail "missing_file path=${path}"
}

echo "==> S02 verify: file presence"
require_file "${TEST_PROJECT}"
require_file "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs"
require_file "${ROOT_DIR}/tests/HappyGymStats.Tests/BlazorApiFailureTests.cs"

echo "==> S02 verify: build targeted test project"
dotnet build "${TEST_PROJECT}" --nologo

echo "==> S02 verify: run Blazor API failure tests"
dotnet test "${TEST_PROJECT}" --nologo --filter "FullyQualifiedName~BlazorApiFailureTests"

echo "==> S02 verify passed"
