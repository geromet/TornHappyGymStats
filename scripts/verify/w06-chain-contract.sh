#!/usr/bin/env bash
# w06-chain-contract.sh — canonical verifier for M008 chain command (workspace/V2/handoff/06).
#
# Numbering note: the handoff calls this "w05-chain-contract.sh". w05 was already taken by
# M007 S05's scouting verifier (the handoff's own "w04" name collided with GSD's
# w04-war-api-hub-board.sh), so every downstream verifier number is +1. This is w06.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

readonly TRACKER_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/ChainTracker.cs"
readonly LAPSE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/ChainLapseInference.cs"
readonly CHAIN_ENGINE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/ChainEngine.cs"
readonly ENGINE_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarStateDerivationEngine.cs"
readonly MODELS_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarDerivedStateModels.cs"
readonly TRACKER_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/ChainTrackerTests.cs"
readonly LAPSE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/ChainLapseInferenceTests.cs"
readonly ENGINE_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarStateDerivationEngineTests.cs"
readonly BOARD_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"

readonly TEST_FILTER="ChainTrackerTests|ChainLapseInferenceTests|WarStateDerivationEngineTests"

required_files=(
  "$TEST_PROJECT"
  "$TRACKER_SOURCE"
  "$LAPSE_SOURCE"
  "$CHAIN_ENGINE_SOURCE"
  "$ENGINE_SOURCE"
  "$MODELS_SOURCE"
  "$TRACKER_TESTS"
  "$LAPSE_TESTS"
  "$ENGINE_TESTS"
  "$BOARD_PAGE"
)

# Acceptance criteria from workspace/V2/handoff/06, each pinned to a named test.
required_tests=(
  # task 1 — the tracker multiplier cannot disagree with the scoring engine
  "CurrentMultiplier_never_disagrees_with_ChainEngine"
  # task 1 — reservation window is the last five hits before a milestone
  "Reservation_window_is_the_last_five_hits_before_a_milestone"
  # task 3 — inferred timer is honest: a chain older than the window reads "unknown", not a full timer
  "A_chain_that_never_rises_in_the_window_reports_unknown_not_a_full_timer"
  # task 3 — a chain that rose then lapsed must not walk a live countdown off a dead chain
  "A_chain_that_rose_then_lapsed_reports_unknown_not_a_walking_countdown"
  # 00-brief assumption ledger — the 300s chain-lapse timeout constant is challengeable
  "ChainLapseInference_timeout_constant_is_challengeable"
  # task 4 — the loudest signal: timer-about-to-lapse outranks the reservation window
  "AlertLevel_timer_running_low_outranks_the_reservation_window"
  # task 7 — chain in the window, nothing attackable => wait/revive, name the forfeited bonus
  "At_995_with_no_war_target_the_advice_is_wait_not_filler_and_names_the_cost"
  "Derive_chain_command_holds_for_a_war_target_when_none_is_attackable_in_the_window"
  # wiring — the board's derived state actually carries the chain command + inferred timer
  "Derive_attaches_chain_command_with_an_inferred_lapse_timer"
  # S01 sweep outcome — Torn's own deadline wins, but never dishonestly
  "Derive_prefers_Torns_own_deadline_over_the_inference"
  "Derive_falls_back_to_inference_when_the_newest_deadline_has_already_passed"
  "Derive_never_gives_the_enemy_an_exact_timer"
  "An_exact_timer_running_low_still_raises_the_alert"
  "A_deadline_already_past_reads_zero_not_a_negative_countdown"
)

# The chain command is pure logic on the public-war read path — no live Torn calls, no transport.
guardrail_files=(
  "$TRACKER_SOURCE"
  "$LAPSE_SOURCE"
)

forbidden_patterns=(
  'TornApiClient'
  'TornRateLimiter'
  'HttpClient'
  'api\.torn\.com'
  'WebApplication'
  'Kestrel'
  'ajax'
  'Centrifugo'
  'scraping'
  'Encrypted'
  'Anonymised'
  'Anonymized'
)

# Board literals the chain panel must keep (parallel to w04's board-literal pins).
board_literals=(
  'Chain command'
  'Landing chain'
  'Outside targets locked'
  'Wait or revive'
  # the label must change with the data source, so an exact countdown is never dressed up as
  # a guess and a guess is never dressed up as a countdown
  'Chain lapses in'
  'Last hit'
)

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

case "${1:-}" in
  -h|--help)
    printf 'Usage: bash scripts/verify/w06-chain-contract.sh\n\nCanonical M008 chain-command verifier: pinned acceptance tests + source boundary guardrails.\n'
    exit 0
    ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${required_files[@]}"; do
  [[ -f "$path" ]] || fail "missing required path ${path#"${ROOT_DIR}/"}"
done
pass "required chain-command paths present"

for test_name in "${required_tests[@]}"; do
  rg -n --fixed-strings "$test_name" "$TRACKER_TESTS" "$LAPSE_TESTS" "$ENGINE_TESTS" >/dev/null \
    || fail "pinned acceptance test '$test_name' not found"
done
pass "all ${#required_tests[@]} pinned acceptance tests present"

for literal in "${board_literals[@]}"; do
  rg -n --fixed-strings "$literal" "$BOARD_PAGE" >/dev/null \
    || fail "chain panel board literal '$literal' missing from War.razor"
done
pass "chain panel board literals present"

for pattern in "${forbidden_patterns[@]}"; do
  if rg -n --glob '!**/bin/**' --glob '!**/obj/**' "$pattern" "${guardrail_files[@]}"; then
    fail "boundary drift: forbidden token '$pattern' in a chain-command source"
  fi
done
pass "chain-command sources stay inside Core/public-war boundary guardrails"

dotnet test "$TEST_PROJECT" --filter "$TEST_FILTER" --nologo
pass "pinned chain-command tests passed (${TEST_FILTER})"

printf 'PASS: canonical M008 chain-command verifier succeeded\n'
