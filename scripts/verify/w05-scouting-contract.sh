#!/usr/bin/env bash
# w05-scouting-contract.sh — canonical verifier for the ranked-war scouting slice
# (GSD milestone M006; workspace/V2/handoff/05-milestone-2-scouting.md).
#
# Pins the hand-off's acceptance criteria:
#   - backfill is resumable and idempotent
#   - no war id is stored twice
#   - a faction profile renders from stored rows with no live Torn calls
#   - milestone-lump detection fires on the DerDoruk / war-48377 fixture
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

readonly ENGINE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/OpponentProfileEngine.cs"
readonly SCOUT_SERVICE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarScoutService.cs"
readonly SCOUT_CONTROLLER="${ROOT_DIR}/src/HappyGymStats.Api/Controllers/WarScoutController.cs"
readonly INGEST_WRITER="${ROOT_DIR}/src/HappyGymStats.Core/War/WarHistoryIngestWriter.cs"
readonly BACKFILL_WORKER="${ROOT_DIR}/src/HappyGymStats.WarPoller/RankedWarHistoryBackfillWorker.cs"
readonly BACKFILL_HOSTED="${ROOT_DIR}/src/HappyGymStats.WarPoller/RankedWarHistoryBackfillHostedService.cs"
readonly SCOUT_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScout.razor"
readonly ENGINE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/OpponentProfileEngineTests.cs"
readonly BACKFILL_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/RankedWarHistoryBackfillServiceTests.cs"
readonly INGEST_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarHistoryIngestWriterTests.cs"
readonly PERSISTENCE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarHistoryPersistenceTests.cs"

readonly TEST_FILTER="OpponentProfileEngineTests|WarScoutServiceTests|WarScoutEndpointTests|WarScoutBlazorServiceTests|WarScoutStaticContractTests|RankedWarHistoryBackfillServiceTests|RankedWarHistoryBackfillFailureTests|RankedWarHistoryBackfillStateRepositoryTests|WarHistoryIngestWriterTests|WarHistoryPersistenceTests"

usage() {
  cat <<'EOF'
Usage: bash scripts/verify/w05-scouting-contract.sh

Runs the canonical scouting verifier:
  1) required source/test files present
  2) hand-off acceptance criteria pinned to named tests
  3) source-only boundary check: the scouting read path makes no live Torn calls
  4) targeted scouting + backfill + ingest tests
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

require_file() {
  [[ -f "$1" ]] || fail "missing required path ${1#"${ROOT_DIR}/"}"
}

require_test() {
  local needle="$1" file="$2" label="$3"
  grep -Fq "$needle" "$file" || fail "acceptance criterion not pinned: ${label} (looked for '${needle}' in ${file#"${ROOT_DIR}/"})"
  pass "$label"
}

required_files=(
  "$TEST_PROJECT" "$ENGINE_SOURCE" "$SCOUT_SERVICE" "$SCOUT_CONTROLLER" "$INGEST_WRITER"
  "$BACKFILL_WORKER" "$BACKFILL_HOSTED" "$SCOUT_PAGE"
  "$ENGINE_TESTS" "$BACKFILL_TESTS" "$INGEST_TESTS" "$PERSISTENCE_TESTS"
)
for path in "${required_files[@]}"; do
  require_file "$path"
done
pass "required scouting source and test files present"

require_test 'UpsertWarAsync_is_idempotent_by_war_id' "$PERSISTENCE_TESTS" \
  "no war id is stored twice"
require_test 'First_run_persists_history_and_reports_second_run_resumes_and_skips_captured_reports' "$BACKFILL_TESTS" \
  "backfill is resumable across restarts"
require_test 'Disabled_hosted_service_makes_no_torn_calls_or_database_writes' "$BACKFILL_TESTS" \
  "backfill is inert while disabled"
require_test 'are_idempotent_and_refresh_capture_timestamps' "$INGEST_TESTS" \
  "history/report ingest is idempotent"
require_test 'BuildProfile_reconstructs_the_DerDoruk_war_' "$ENGINE_TESTS" \
  "milestone-lump detection fires on the DerDoruk / war-48377 fixture"
require_test 'BuildProfile_does_not_flag_a_strong_above_median_member_with_no_lump' "$ENGINE_TESTS" \
  "lump detection does not flag a strong lumpless member (tolerance lower edge)"
require_test 'BuildProfile_flags_a_lump_on_a_member_who_also_hits_above_the_faction_median' "$ENGINE_TESTS" \
  "lump detection flags a lump on an above-median member (tolerance upper edge)"
require_test 'GetProfileAsync_returns_null_when_no_captured_history_exists_for_the_faction' \
  "${ROOT_DIR}/tests/HappyGymStats.Tests/WarScoutServiceTests.cs" \
  "profile is built only from captured history"

# The scouting read path (engine, Core service, controller, page) must never reach Torn directly:
# "a faction's profile page renders from stored data with no live Torn calls" (hand-off 05).
readonly CORE_READ_PATH=("$ENGINE_SOURCE" "$SCOUT_SERVICE" "$SCOUT_CONTROLLER")
readonly CORE_FORBIDDEN=(TornApiClient TornRateLimiter HttpClient 'api.torn.com' centrifugo Centrifugo scraping)
for file in "${CORE_READ_PATH[@]}"; do
  for token in "${CORE_FORBIDDEN[@]}"; do
    if grep -Fq "$token" "$file"; then
      fail "scouting read path ${file#"${ROOT_DIR}/"} references forbidden token '$token'"
    fi
  done
done
# The Blazor page legitimately uses an HttpClient-backed service, so only Torn/transport tokens
# are forbidden there.
for token in TornApiClient 'api.torn.com' centrifugo Centrifugo scraping; do
  if grep -Fq "$token" "$SCOUT_PAGE"; then
    fail "scout page references forbidden token '$token'"
  fi
done
pass "scouting read path renders from stored data with no live Torn calls"

grep -Fq 'IWarHistoryRepository' "$SCOUT_SERVICE" \
  || fail "WarScoutService should read through IWarHistoryRepository"
pass "WarScoutService reads stored rows through IWarHistoryRepository"

echo
echo "[w05] dotnet test --filter '${TEST_FILTER}'"
dotnet test "$TEST_PROJECT" --filter "$TEST_FILTER" --nologo
pass "targeted scouting / backfill / ingest tests passed"

printf 'PASS: canonical w05 scouting verifier succeeded\n'

# ---------------------------------------------------------------------------
# KNOWN GAP: milestone-lump detection tolerance (OpponentProfileEngine's
# LumpResidualToleranceFraction / MinDetectableLumpBonus) is validated only
# against synthetic fixtures here. Its real-world flag rate on a full 71-member
# roster is unmeasured because no backfilled war history exists locally.
#
# When a populated DB is available, sanity-check the flag rate with e.g.:
#   dotnet run --project tools/scout-flag-rate -- --connection "<conn>" --faction <id>
# (tool not yet built) — expect only a small single-digit % of (member,war)
# rows flagged; a high rate means the tolerance is too loose.
# ---------------------------------------------------------------------------
