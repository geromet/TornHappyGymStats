#!/usr/bin/env bash
# w03-war-derived-state-and-holes.sh — canonical verifier for S03 derived war state math and boundary guardrails.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
readonly W02_VERIFIER="${ROOT_DIR}/scripts/verify/w02-war-persistence-and-poller.sh"
readonly ENGINE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarStateDerivationEngine.cs"
readonly MODELS_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarDerivedStateModels.cs"
readonly SERVICE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarDerivedStateService.cs"
readonly ENGINE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarStateDerivationEngineTests.cs"
readonly SERVICE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarDerivedStateServiceTests.cs"
readonly TEST_FILTER="WarStateDerivationEngineTests|WarDerivedStateServiceTests"

required_files=(
  "$TEST_PROJECT"
  "$ENGINE_SOURCE"
  "$MODELS_SOURCE"
  "$SERVICE_SOURCE"
  "$ENGINE_TESTS"
  "$SERVICE_TESTS"
)

guardrail_files=(
  "$ENGINE_SOURCE"
  "$MODELS_SOURCE"
  "$SERVICE_SOURCE"
)

forbidden_patterns=(
  'TornApiClient'
  'HttpClient'
  'WebApplication'
  'Kestrel'
  'ajax'
  'Centrifugo'
  'scraping'
  'Encrypted'
  'Anonymised'
  'Anonymized'
)

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/w03-war-derived-state-and-holes.sh [--with-w02]

Runs the canonical S03 verifier:
- targeted derived-war tests only
- source-only boundary guardrails on S03 derived-state files
- optional S02 baseline verifier when explicitly requested

Options:
  --with-w02   Run scripts/verify/w02-war-persistence-and-poller.sh first.
EOF
}

pass() {
  printf 'PASS: %s\n' "$1"
}

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

run_w02=false
for arg in "$@"; do
  case "$arg" in
    --with-w02)
      run_w02=true
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown option '$arg'"
      ;;
  esac
done

for path in "${required_files[@]}"; do
  [[ -f "$path" ]] || fail "missing required path ${path#"${ROOT_DIR}/"}"
done
pass "required paths present for S03 derived-state verifier"

rg -n 'public sealed class WarStateDerivationEngineTests|public sealed class WarDerivedStateServiceTests' \
  "$ENGINE_TESTS" "$SERVICE_TESTS" >/dev/null \
  || fail "targeted test classes missing from derived-state test files"
pass "targeted derived-state test classes present"

if [[ "$run_w02" == true ]]; then
  [[ -x "$W02_VERIFIER" || -f "$W02_VERIFIER" ]] || fail "optional S02 baseline verifier missing at scripts/verify/w02-war-persistence-and-poller.sh"
  bash "$W02_VERIFIER"
  pass "optional S02 baseline verifier passed"
else
  pass "skipped optional S02 baseline verifier (use --with-w02 to enable)"
fi

for pattern in "${forbidden_patterns[@]}"; do
  if rg -n --glob '!**/bin/**' --glob '!**/obj/**' "$pattern" "${guardrail_files[@]}"; then
    fail "boundary drift detected for forbidden token '$pattern' in S03 derived-state sources"
  fi
done
pass "derived-state sources stay inside Core/public-war boundary guardrails"

dotnet test "$TEST_PROJECT" --filter "$TEST_FILTER" --nologo
pass "targeted derived-state tests passed (${TEST_FILTER})"

printf 'PASS: canonical S03 verifier succeeded\n'
