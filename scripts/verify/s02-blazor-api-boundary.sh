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

require_grep() {
  local pattern="$1"
  local path="$2"
  if ! rg -q "$pattern" "$path"; then
    fail "missing_pattern path=${path} pattern=${pattern}"
  fi
}

print_operator_gate() {
  cat <<'EOF'
==> S02 operator gate: Keycloak checkpoint
Pause auto-mode and apply manual Keycloak fixes when any of these are observed:
  - GET /api/v1/torn/surfaces/me returns 401 for signed-in caller
  - GET /api/v1/torn/surfaces/me returns 403 for signed-in caller
  - API indicates missing/invalid anonymous_id claim for authenticated caller
Resume criteria:
  - signed-in /my-stats request succeeds without auth challenge loop
  - GET /api/v1/torn/surfaces/me returns 200 for signed-in caller
  - response data remains caller-scoped and claim-bound (no PlayerID input contract)
EOF
}

echo "==> S02 verify: file presence"
require_file "${TEST_PROJECT}"
require_file "${ROOT_DIR}/src/HappyGymStats.Api/Controllers/SurfacesController.cs"
require_file "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor"
require_file "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs"
require_file "${ROOT_DIR}/tests/HappyGymStats.Tests/BlazorApiFailureTests.cs"

echo "==> S02 verify: auth/data boundary contract markers"
require_grep '\[HttpGet\("me"\)\]' "${ROOT_DIR}/src/HappyGymStats.Api/Controllers/SurfacesController.cs"
require_grep '\[Authorize\(Roles = Roles.User\)\]' "${ROOT_DIR}/src/HappyGymStats.Api/Controllers/SurfacesController.cs"
require_grep 'FindFirstValue\(Claims.AnonymousId\)' "${ROOT_DIR}/src/HappyGymStats.Api/Controllers/SurfacesController.cs"
require_grep '@page "/my-stats"' "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor"
require_grep '@attribute \[Authorize\]' "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor"

echo "==> S02 verify: build targeted test project"
dotnet build "${TEST_PROJECT}" --nologo

echo "==> S02 verify: run Blazor API failure tests"
dotnet test "${TEST_PROJECT}" --nologo --filter "FullyQualifiedName~BlazorApiFailureTests"

print_operator_gate

echo "==> S02 verify passed"
