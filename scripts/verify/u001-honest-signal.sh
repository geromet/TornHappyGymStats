#!/usr/bin/env bash
# u001-honest-signal.sh — an estimate must never look like a fact (U001; issue #94).
#
# WHAT THIS PINS, AND WHY IT IS SHAPED THIS WAY
#
# The obvious verifier — grep the page for weasel words like "approx" or "~" —
# would fire on the inferred chain timer's "~mm:ss ago (±30s)", which is correct,
# already shipped, and exactly the honesty this rule is about. A check that makes
# you weaken the thing it protects is worse than no check.
#
# So it pins the COMPONENT instead: every figure on the war board renders through
# <Figure>, which cannot omit the marker. The failure mode being prevented is not
# a bad word; it is the next person adding a panel that quietly prints a number.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

readonly BOARD="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"
readonly FIGURE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Shared/Figure.razor"
readonly KINDS="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Shared/FigureKind.cs"
readonly STYLES="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/wwwroot/app.css"

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

case "${1:-}" in
  -h|--help) printf 'Usage: bash scripts/verify/u001-honest-signal.sh\n\nOffline check that every war-board figure declares whether it is measured, projected or inferred.\n'; exit 0 ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${BOARD}" "${FIGURE}" "${KINDS}" "${STYLES}"; do
  [[ -f "${path}" ]] || fail "missing ${path#"${ROOT_DIR}/"}"
done
pass "honest-signal files present"

# The vocabulary is three words and lives in one place. A fourth kind, or a
# renamed one, has to be a deliberate edit here.
for kind in Measured Projected Inferred; do
  rg -q "^    ${kind}," "${KINDS}" || fail "FigureKind.${kind} is missing — the vocabulary changed without the plan"
done
if rg -q '^    [A-Z][A-Za-z]+,' "${KINDS}"; then
  count="$(rg -c '^    [A-Z][A-Za-z]+,' "${KINDS}")"
  [[ "${count}" == "3" ]] || fail "FigureKind has ${count} values; U001 defines exactly three (see issue #94)"
fi
pass "the vocabulary is exactly measured / projected / inferred"

# Measured figures deliberately carry NO marker: marking everything is the same
# as marking nothing. If that inverts, the markers stop meaning anything.
rg -q 'NeedsMarker.*Projected or FigureKind.Inferred' "${KINDS}" \
  || fail "NeedsMarker no longer limits markers to projected and inferred figures"
pass "only projected and inferred figures carry a marker"

# The two extrapolations that read as facts on a war night. These are the whole
# reason for the slice: "ETA 00:12:00" was rendered exactly like a score.
rg -q 'Label="ETA".*\n?.*FigureKind.Projected' -U "${BOARD}" \
  || fail "the ETA figure is not marked Projected"
rg -q 'Label="Attacks to finish".*\n?.*FigureKind.Projected' -U "${BOARD}" \
  || fail "the attacks-to-finish figure is not marked Projected"
pass "ETA and attacks-to-finish declare themselves projected"

# The chain timer is the case the vocabulary was derived from: Torn's own
# deadline is measured, the poll-history estimate is inferred.
rg -q 'ChainTimerKind' "${BOARD}" || fail "the chain timer no longer selects its kind"
rg -q 'TimerConfidence == "Exact" \? FigureKind.Measured : FigureKind.Inferred' "${BOARD}" \
  || fail "the chain timer no longer distinguishes an exact deadline from an inferred one"
pass "the chain timer keeps its exact/inferred distinction"

# Hole severity is a proxy until a linked key supplies real energy (M009).
rg -q 'hgs-figure-marker-inferred' "${BOARD}" \
  || fail "the hole-alerts panel no longer declares itself inferred"
pass "hole severity declares itself inferred"

# No war-board FIGURE may bypass the component. A raw @faction./@chain. binding
# inside MudText is the pattern that printed an estimate as a fact.
#
# Prose is not a figure and is exempt by name, not by loosening the pattern: a
# faction's name and the chain advice sentence carry no numeric claim, so a
# provenance marker on them would be noise. Anything NOT on this list is a new
# binding somebody added, and has to be classified deliberately — which is the
# whole point of the check.
readonly NON_FIGURE_BINDINGS='faction\.FactionName|faction\.FactionId|chain\.Advice|chain\.TimerDiagnostic'

unrouted="$(rg -n '<MudText[^>]*>@(faction|chain)\.' "${BOARD}" | rg -v "${NON_FIGURE_BINDINGS}" || true)"
if [[ -n "${unrouted}" ]]; then
  printf '%s\n' "${unrouted}"
  fail "a war-board value renders outside <Figure> — route it through the component, or add it to NON_FIGURE_BINDINGS if it carries no numeric claim"
fi
pass "every war-board figure goes through <Figure> (prose exempted by name)"

# The marker must not be colour alone.
rg -q 'border: 1px solid currentColor' "${STYLES}" \
  || fail "figure markers rely on colour alone; keep the border so the distinction survives colour blindness"
pass "markers do not rely on colour alone"

printf 'U001_HONEST_SIGNAL_PASS\n'
