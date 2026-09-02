#!/usr/bin/env bash
# w04-war-api-hub-board.sh — canonical verifier for S04 war API + hub + board final assembly.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
readonly SOLUTION_FILE="${ROOT_DIR}/HappyGymStats.sln"
readonly W01_VERIFIER="${ROOT_DIR}/scripts/verify/w01-war-core-api-foundation.sh"
readonly W02_VERIFIER="${ROOT_DIR}/scripts/verify/w02-war-persistence-and-poller.sh"
readonly W03_VERIFIER="${ROOT_DIR}/scripts/verify/w03-war-derived-state-and-holes.sh"
readonly API_PROGRAM="${ROOT_DIR}/src/HappyGymStats.Api/Program.cs"
readonly WAR_CONTROLLER="${ROOT_DIR}/src/HappyGymStats.Api/Controllers/WarController.cs"
readonly HUB_SOURCE="${ROOT_DIR}/src/HappyGymStats.Api/Hubs/WarHub.cs"
readonly HUB_BROADCASTER="${ROOT_DIR}/src/HappyGymStats.Api/Hubs/WarHubBroadcaster.cs"
readonly BLAZOR_DTOS="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/WarDtos.cs"
readonly BOARD_SERVICE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarBoardService.cs"
readonly BOARD_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"
readonly LAYOUT_FILE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor"
readonly NGINX_CONF="${ROOT_DIR}/infra/nginx-torn.conf"
readonly ENV_EXAMPLE="${ROOT_DIR}/infra/.env.example"
readonly FINAL_GUARDRAIL_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarFinalAssemblyGuardrailTests.cs"

pass() {
  printf 'PASS: %s\n' "$1"
}

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

require_file() {
  local file="$1"
  [[ -f "$file" ]] || fail "missing file $file"
  pass "found $(basename "$file")"
}

require_literal() {
  local needle="$1"
  local file="$2"
  local label="$3"
  grep -Fq "$needle" "$file" || fail "$label"
  pass "$label"
}

require_regex() {
  local pattern="$1"
  local file="$2"
  local label="$3"
  grep -Eq "$pattern" "$file" || fail "$label"
  pass "$label"
}

forbid_regex_in_files() {
  local pattern="$1"
  local label="$2"
  shift 2
  local log_file
  log_file="$(mktemp "${ROOT_DIR}/.w04-forbidden.XXXXXX")"
  if rg -n -i "$pattern" "$@" >"$log_file" 2>&1; then
    cat "$log_file" >&2
    rm -f "$log_file"
    fail "$label"
  fi
  rm -f "$log_file"
  pass "$label"
}

run_cmd() {
  local label="$1"
  shift
  local log_file
  log_file="$(mktemp "${ROOT_DIR}/.w04-run.XXXXXX")"
  if "$@" >"$log_file" 2>&1; then
    rm -f "$log_file"
    pass "$label"
    return 0
  fi

  tail -n 60 "$log_file" >&2 || true
  rm -f "$log_file"
  fail "$label"
}

check_hub_route_order() {
  local hub_line api_line
  hub_line="$(grep -nF 'location /api/hub/war {' "$NGINX_CONF" | cut -d: -f1 | head -n1)"
  api_line="$(grep -nF 'location /api/ {' "$NGINX_CONF" | cut -d: -f1 | head -n1)"

  [[ -n "$hub_line" && -n "$api_line" ]] || fail "nginx hub/api locations detected"
  (( hub_line < api_line )) || fail "nginx matches /api/hub/war before /api/"
  pass "nginx matches /api/hub/war before /api/"
}

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/w04-war-api-hub-board.sh

Runs deterministic final-assembly checks for S04:
  1) w01, w02, and w03 canonical verifiers
  2) targeted S04 tests (API/hub endpoint, poller notification, board static, final guardrails)
  3) dotnet build HappyGymStats.sln
  4) static guardrails for hub route registration, /war route/nav, nginx WebSocket wiring,
     stale-health + hole + coverage labels, env example loopback notify settings,
     and absence of direct Torn/ajax/Centrifugo/scraping/personal-lane dependencies
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

require_file "$W01_VERIFIER"
require_file "$W02_VERIFIER"
require_file "$W03_VERIFIER"
require_file "$TEST_PROJECT"
require_file "$SOLUTION_FILE"
require_file "$API_PROGRAM"
require_file "$WAR_CONTROLLER"
require_file "$HUB_SOURCE"
require_file "$HUB_BROADCASTER"
require_file "$BLAZOR_DTOS"
require_file "$BOARD_SERVICE"
require_file "$BOARD_PAGE"
require_file "$LAYOUT_FILE"
require_file "$NGINX_CONF"
require_file "$ENV_EXAMPLE"
require_file "$FINAL_GUARDRAIL_TESTS"

run_cmd "w01 canonical verifier" bash "$W01_VERIFIER"
run_cmd "w02 canonical verifier" bash "$W02_VERIFIER"
run_cmd "w03 canonical verifier" bash "$W03_VERIFIER"
run_cmd "WarApiHubEndpointTests" dotnet test "$TEST_PROJECT" --nologo --filter FullyQualifiedName~WarApiHubEndpointTests
run_cmd "WarPollerHubNotificationTests" dotnet test "$TEST_PROJECT" --nologo --filter FullyQualifiedName~WarPollerHubNotificationTests
run_cmd "WarBoardStaticContractTests" dotnet test "$TEST_PROJECT" --nologo --filter FullyQualifiedName~WarBoardStaticContractTests
run_cmd "WarFinalAssemblyGuardrailTests" dotnet test "$TEST_PROJECT" --nologo --filter FullyQualifiedName~WarFinalAssemblyGuardrailTests
run_cmd "dotnet build HappyGymStats.sln" dotnet build "$SOLUTION_FILE" --nologo

require_literal 'builder.Services.AddSignalR(options =>' "$API_PROGRAM" 'SignalR registered in HappyGymStats.Api'
require_literal 'app.MapHub<WarHub>("/api/hub/war");' "$API_PROGRAM" 'War hub mapped at /api/hub/war'
require_literal 'location /api/hub/war {' "$NGINX_CONF" 'Dedicated nginx /api/hub/war location present'
check_hub_route_order
require_regex 'proxy_set_header[[:space:]]+Upgrade[[:space:]]+\$http_upgrade;' "$NGINX_CONF" 'nginx forwards WebSocket Upgrade header'
require_regex 'proxy_set_header[[:space:]]+Connection[[:space:]]+"upgrade";' "$NGINX_CONF" 'nginx forwards WebSocket Connection header'
require_literal '@page "/war"' "$BOARD_PAGE" 'War board route declared'
require_literal 'Href="/war"' "$LAYOUT_FILE" 'War board nav entry present'
require_literal 'Coverage ratio' "$BOARD_PAGE" 'Coverage ratio label present'
require_literal 'Hole alerts' "$BOARD_PAGE" 'Hole alerts label present'
require_literal 'Stale data.' "$BOARD_PAGE" 'Stale-data banner label present'
require_literal 'WarPoller__HubNotifyUrl=' "$ENV_EXAMPLE" 'Loopback notify URL documented in env example'
require_literal '/api/v1/war/internal/notify' "$ENV_EXAMPLE" 'Internal notify endpoint documented in env example'

forbid_regex_in_files 'TornApiClient|api\.torn\.com|centrifugo|ajax|scrap|PersonalLane|personal-lane' \
  'No forbidden direct Torn/ajax/Centrifugo/scraping/personal-lane references in API hub + board boundary files' \
  "$WAR_CONTROLLER" "$HUB_SOURCE" "$HUB_BROADCASTER" "$BLAZOR_DTOS" "$BOARD_SERVICE" "$BOARD_PAGE"

pass 'w04 war API + hub + board final assembly verifier passed'
