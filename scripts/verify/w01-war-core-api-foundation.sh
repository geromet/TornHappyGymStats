#!/usr/bin/env bash
# w01-war-core-api-foundation.sh — deterministic verifier for S01 war-core boundary.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
readonly APPROVED_TORN_CLIENT="${ROOT_DIR}/src/HappyGymStats.Core/Torn/TornApiClient.cs"
readonly APPROVED_CHAIN_ENGINE="${ROOT_DIR}/src/HappyGymStats.Core/War/ChainEngine.cs"

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/w01-war-core-api-foundation.sh

Runs deterministic checks for the S01 war-core API foundation boundary:
  1) Required source/test/fixture presence checks
  2) dotnet build on the test project
  3) targeted WarFixtureContractTests, WarTornApiClientTests, and ChainEngineTests
  4) static boundary checks for a single Core TornApiClient and Core-owned ChainEngine
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

fail() {
  echo "[w01][fail] $*" >&2
  exit 1
}

phase() {
  echo
  echo "[w01] $*"
}

require_file() {
  local path="$1"
  [[ -f "$path" ]] || fail "missing required file: ${path#"${ROOT_DIR}/"}"
}

run_targeted_test() {
  local filter="$1"
  local label="$2"
  local log_file
  log_file="$(mktemp)"

  echo "[w01] running ${label} (${filter})"
  if ! dotnet test "$TEST_PROJECT" --nologo --no-build --filter "$filter" >"$log_file" 2>&1; then
    cat "$log_file"
    rm -f "$log_file"
    fail "${label} failed"
  fi

  if grep -Eq "No test matches|0 tests|Total tests: 0" "$log_file"; then
    cat "$log_file"
    rm -f "$log_file"
    fail "${label} matched no tests; filter drifted"
  fi

  cat "$log_file"
  rm -f "$log_file"
}

phase "war core API foundation verifier"
cd "$ROOT_DIR"

phase "checking required files"
require_file "$TEST_PROJECT"
require_file "$APPROVED_TORN_CLIENT"
require_file "$APPROVED_CHAIN_ENGINE"
require_file "$ROOT_DIR/src/HappyGymStats.Core/War/WarEndpointModels.cs"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/WarFixtureContractTests.cs"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/WarTornApiClientTests.cs"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/ChainEngineTests.cs"
require_file "$ROOT_DIR/tests/fixtures/war/live-faction-wars.json"
require_file "$ROOT_DIR/tests/fixtures/war/ranked-war-report-48377.json"
require_file "$ROOT_DIR/tests/fixtures/war/global-ranked-wars-live.json"
require_file "$ROOT_DIR/tests/fixtures/war/user-attacks-page.json"

phase "building test project"
command -v dotnet >/dev/null 2>&1 || fail "dotnet SDK not found in PATH"
dotnet build "$TEST_PROJECT" --nologo

phase "running targeted tests"
run_targeted_test "FullyQualifiedName~WarFixtureContractTests" "War fixture contract tests"
run_targeted_test "FullyQualifiedName~WarTornApiClientTests" "War Torn API client tests"
run_targeted_test "FullyQualifiedName~ChainEngineTests" "ChainEngine tests"

phase "checking TornApiClient ownership"
mapfile -t TORN_CLIENT_FILES < <(find "$ROOT_DIR/src" -type f -name 'TornApiClient.cs' | sort)
if [[ ${#TORN_CLIENT_FILES[@]} -ne 1 ]]; then
  printf '%s\n' "${TORN_CLIENT_FILES[@]:-}" >&2
  fail "expected exactly one TornApiClient.cs under src/, found ${#TORN_CLIENT_FILES[@]}"
fi
if [[ "${TORN_CLIENT_FILES[0]}" != "$APPROVED_TORN_CLIENT" ]]; then
  printf '%s\n' "${TORN_CLIENT_FILES[@]}" >&2
  fail "TornApiClient.cs moved or duplicated outside src/HappyGymStats.Core/Torn"
fi

phase "checking ChainEngine ownership"
mapfile -t CHAIN_ENGINE_FILES < <(find "$ROOT_DIR/src" -type f -name 'ChainEngine.cs' | sort)
if [[ ${#CHAIN_ENGINE_FILES[@]} -ne 1 ]]; then
  printf '%s\n' "${CHAIN_ENGINE_FILES[@]:-}" >&2
  fail "expected exactly one ChainEngine.cs under src/, found ${#CHAIN_ENGINE_FILES[@]}"
fi
if [[ "${CHAIN_ENGINE_FILES[0]}" != "$APPROVED_CHAIN_ENGINE" ]]; then
  printf '%s\n' "${CHAIN_ENGINE_FILES[@]}" >&2
  fail "ChainEngine.cs must live under src/HappyGymStats.Core/War"
fi
if grep -R -n -E '(^|[[:space:]])(class|record)[[:space:]]+ChainEngine\b|(^|[[:space:]])record[[:space:]]+ChainSplit\b' "$ROOT_DIR/src/HappyGymStats.Blazor"; then
  fail "Blazor project contains ChainEngine/ChainSplit implementation code; Core must remain the single source of truth"
fi

phase "all S01 war-core boundary checks passed"
