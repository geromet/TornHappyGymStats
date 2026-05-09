#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

CURRENT_SECTION="bootstrap"
CURRENT_CMD=""

on_error() {
  local exit_code=$?
  echo "[FAIL] section=${CURRENT_SECTION} command=${CURRENT_CMD}" >&2
  exit "$exit_code"
}
trap on_error ERR

run_cmd() {
  local cmd="$1"
  CURRENT_CMD="$cmd"
  eval "$cmd"
}

section() {
  CURRENT_SECTION="$1"
  echo
  echo "==> ${CURRENT_SECTION}"
}

require_token() {
  local file="$1"
  local token="$2"
  local label="$3"
  if ! grep -Fq -- "$token" "$file"; then
    CURRENT_CMD="require_token:${label}"
    echo "[FAIL] missing token (${label}) in ${file}" >&2
    return 1
  fi
  echo "[PASS] ${label}"
}

require_regex() {
  local file="$1"
  local regex="$2"
  local label="$3"
  if ! rg -q --pcre2 "$regex" "$file"; then
    CURRENT_CMD="require_regex:${label}"
    echo "[FAIL] missing marker (${label}) in ${file}" >&2
    return 1
  fi
  echo "[PASS] ${label}"
}

section "build"
run_cmd "dotnet build '${TEST_PROJECT}' --nologo --no-restore"

section "auth-contract-tests"
run_cmd "dotnet test '${TEST_PROJECT}' --nologo --no-build --filter 'FullyQualifiedName~M004FinalGateTests|FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests'"

section "static-auth-and-endpoint-markers"
require_token "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor" "@attribute [Authorize]" "My stats page has [Authorize]"
require_token "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor" "MudNavLink Href=\"/my-stats\"" "My stats nav link exists"
require_token "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor" "Icons.Material.Filled.Lock" "My stats nav lock icon exists"
require_token "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs" "private const string MyStatsEndpoint = \"/api/v1/torn/surfaces/me\";" "SurfacesService uses /surfaces/me"
require_token "${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs" "private const string MyStatsImportEndpoint = \"/api/v1/torn/import-jobs/me\";" "SurfacesService uses /import-jobs/me"
require_regex "${ROOT_DIR}/tests/HappyGymStats.Tests/M004FinalGateTests.cs" "Assert\\.DoesNotContain\\(secret,\s*failure\\.Message" "redaction assertion on failure.Message"
require_regex "${ROOT_DIR}/tests/HappyGymStats.Tests/M004FinalGateTests.cs" "Assert\\.DoesNotContain\\(secret,\s*failure\\.SafeMessage" "redaction assertion on failure.SafeMessage"

section "docs-operator-gate"
run_cmd "bash '${ROOT_DIR}/scripts/verify/s08-docs-contract.sh'"

section "operator-runbook-markers"
require_token "${ROOT_DIR}/docs/M004-MY-STATS-OPERATOR-GATE.md" "signed-out" "runbook includes signed-out scenario"
require_token "${ROOT_DIR}/docs/M004-MY-STATS-OPERATOR-GATE.md" "identity_setup_required" "runbook includes identity setup blocker"
require_token "${ROOT_DIR}/docs/M004-MY-STATS-OPERATOR-GATE.md" "/api/v1/torn/surfaces/me" "runbook includes /surfaces/me"
require_token "${ROOT_DIR}/docs/M004-MY-STATS-OPERATOR-GATE.md" "/api/v1/torn/import-jobs/me" "runbook includes /import-jobs/me"
require_token "${ROOT_DIR}/docs/M004-MY-STATS-OPERATOR-GATE.md" "sanitized" "runbook includes sanitized evidence guidance"

section "provenance-regression"
run_cmd "bash '${ROOT_DIR}/scripts/verify/s06-provenance-warnings.sh'"

echo
echo "M004 final gate passed."