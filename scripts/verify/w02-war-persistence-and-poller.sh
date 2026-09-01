#!/usr/bin/env bash
# w02-war-persistence-and-poller.sh — deterministic verifier for S02 war persistence and singleton poller boundaries.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
readonly WORKER_PROJECT="${ROOT_DIR}/src/HappyGymStats.WarPoller/HappyGymStats.WarPoller.csproj"
readonly WORKER_SOURCE_DIR="${ROOT_DIR}/src/HappyGymStats.WarPoller"
readonly APPROVED_TORN_CLIENT="${ROOT_DIR}/src/HappyGymStats.Core/Torn/TornApiClient.cs"

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/w02-war-persistence-and-poller.sh

Runs deterministic checks for the S02 public-war persistence + singleton worker boundary:
  1) Required source/test file presence checks
  2) dotnet build on HappyGymStats.WarPoller
  3) targeted WarPersistenceTests, WarPollerServiceTests, and WarPollerHostTests
  4) explicit static checks for a single approved Torn polling client,
     no ASP.NET/Kestrel/WebApplication listener surface, and
     no ajax/Centrifugo/scraping Torn data sources
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

fail() {
  echo "W02_VERIFY_FAIL: $*" >&2
  exit 1
}

note() {
  echo "[w02] $*"
}

require_file() {
  local path="$1"
  [[ -f "$path" ]] || fail "required file missing: ${path#${ROOT_DIR}/}"
}

run() {
  note "RUN $*"
  "$@"
}

assert_zero_matches() {
  local label="$1"
  local pattern="$2"
  shift 2

  local matches
  matches="$(rg -n -i --glob '!bin/**' --glob '!obj/**' --glob '!*Designer.cs' "$pattern" "$@" || true)"
  if [[ -n "$matches" ]]; then
    echo "$matches" >&2
    fail "$label"
  fi

  note "PASS static check: $label"
}

assert_exact_match_count() {
  local expected_count="$1"
  local label="$2"
  local pattern="$3"
  shift 3

  local matches count
  matches="$(rg -n --glob '!bin/**' --glob '!obj/**' "$pattern" "$@" || true)"
  if [[ -z "$matches" ]]; then
    count=0
  else
    count="$(printf '%s\n' "$matches" | wc -l | tr -d ' ')"
  fi

  if [[ "$count" != "$expected_count" ]]; then
    if [[ -n "$matches" ]]; then
      echo "$matches" >&2
    fi
    fail "$label (expected ${expected_count}, found ${count})"
  fi

  note "PASS static check: $label"
}

require_file "$TEST_PROJECT"
require_file "$WORKER_PROJECT"
require_file "$APPROVED_TORN_CLIENT"
require_file "$WORKER_SOURCE_DIR/Program.cs"
require_file "$WORKER_SOURCE_DIR/WarPollerHostedService.cs"
require_file "$WORKER_SOURCE_DIR/WarPollerService.cs"
require_file "$WORKER_SOURCE_DIR/WarPollerOptions.cs"
require_file "$WORKER_SOURCE_DIR/appsettings.json"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/WarPersistenceTests.cs"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/WarPollerServiceTests.cs"
require_file "$ROOT_DIR/tests/HappyGymStats.Tests/WarPollerHostTests.cs"

run dotnet build "$WORKER_PROJECT"
run dotnet test "$TEST_PROJECT" --filter "FullyQualifiedName~HappyGymStats.Tests.WarPersistenceTests"
run dotnet test "$TEST_PROJECT" --filter "FullyQualifiedName~HappyGymStats.Tests.WarPollerServiceTests"
run dotnet test "$TEST_PROJECT" --filter "FullyQualifiedName~HappyGymStats.Tests.WarPollerHostTests"

assert_exact_match_count 1 \
  "approved TornApiClient implementation remains singleton under src/" \
  'class\s+TornApiClient\b' \
  "$ROOT_DIR/src"

assert_exact_match_count 1 \
  "WarPoller registers TornApiClient exactly once" \
  'AddHttpClient\s*<\s*TornApiClient\s*>' \
  "$WORKER_SOURCE_DIR/Program.cs"

assert_zero_matches \
  "WarPoller does not declare alternate Torn polling client types" \
  'class\s+\w*Torn\w*Client\b' \
  "$WORKER_SOURCE_DIR"

assert_zero_matches \
  "WarPoller has no ASP.NET or HTTP listener startup surface" \
  'WebApplication|Kestrel|UseKestrel|ConfigureWebHostDefaults|MapGet|MapPost|MapControllers|AddControllers|AddEndpointsApiExplorer' \
  "$WORKER_SOURCE_DIR"

assert_zero_matches \
  "WarPoller does not reference internal Torn ajax/Centrifugo/scraping sources" \
  'ajax|centrifugo|scrap(e|ing)' \
  "$WORKER_SOURCE_DIR" \
  "$ROOT_DIR/tests/HappyGymStats.Tests/WarPollerServiceTests.cs" \
  "$ROOT_DIR/tests/HappyGymStats.Tests/WarPollerHostTests.cs"

note "All S02 war persistence/poller boundary checks passed."
