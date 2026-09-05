#!/usr/bin/env bash
# fleet-archive-contract.sh — deterministic structural contract for the Git-versioned fleet archive.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly ARCHITECTURE="${ROOT_DIR}/docs/fleet/SELF-IMPROVING-FLEET.md"
readonly ACTIVITY="${ROOT_DIR}/docs/fleet/archive/activity/2026-09.md"
readonly CHANGES="${ROOT_DIR}/docs/fleet/archive/instruction-changes.md"

fail() {
  printf 'FAIL: fleet archive contract: %s\n' "$*" >&2
  exit 1
}

require_file() {
  [[ -f "$1" ]] || fail "missing required file: ${1#"${ROOT_DIR}/"}"
}

require_literal() {
  local file="$1"
  local literal="$2"
  grep -Fq -- "${literal}" "${file}" \
    || fail "${file#"${ROOT_DIR}/"} missing required contract text: ${literal}"
}

require_file "${ARCHITECTURE}"
require_file "${ACTIVITY}"
require_file "${CHANGES}"

# Keep the durable files and live trackers wired together in the architecture contract.
require_literal "${ARCHITECTURE}" 'docs/fleet/archive/activity/YYYY-MM.md'
require_literal "${ARCHITECTURE}" 'docs/fleet/archive/instruction-changes.md'
require_literal "${ARCHITECTURE}" '#170'
require_literal "${ARCHITECTURE}" '#171'
require_literal "${ARCHITECTURE}" 'no fleet merge into a repository default branch'
require_literal "${ARCHITECTURE}" 'truthful evidence / no invented verification'

# Activity snapshots and prompt-change records are intentionally separate durable streams.
if grep -Fq 'FLEET-PROMPT-CHANGE |' "${ACTIVITY}"; then
  fail "activity archive contains a prompt-change marker"
fi
if grep -Fq 'FLEET-SNAPSHOT |' "${CHANGES}"; then
  fail "instruction-change archive contains an activity snapshot marker"
fi

# Every activity heading must own exactly one snapshot marker and retain the minimum
# fields needed to understand scope, delivered work, research, coordination, and next pressure.
awk '
function finish_entry() {
  if (!in_entry) return
  if (snapshot != 1) {
    printf "FAIL: fleet archive contract: activity entry %s has %d FLEET-SNAPSHOT markers\n", heading, snapshot > "/dev/stderr"
    exit 1
  }
  if (!repos || !shipped || !research || !coordination || !next_pressure) {
    printf "FAIL: fleet archive contract: activity entry %s is missing one of repos/shipped/research/coordination/next-pressure\n", heading > "/dev/stderr"
    exit 1
  }
}
/^## / {
  finish_entry()
  in_entry = 1
  heading = substr($0, 4)
  snapshot = repos = shipped = research = coordination = next_pressure = 0
  next
}
in_entry && /^FLEET-SNAPSHOT \| period=/ { snapshot++; next }
in_entry && /^repos:/ { repos = 1; next }
in_entry && /^shipped:/ { shipped = 1; next }
in_entry && /^research:/ { research = 1; next }
in_entry && /^coordination:/ { coordination = 1; next }
in_entry && /^next-pressure:/ { next_pressure = 1; next }
END {
  finish_entry()
  if (!in_entry) {
    print "FAIL: fleet archive contract: activity archive has no dated entries" > "/dev/stderr"
    exit 1
  }
}
' "${ACTIVITY}"

# Every instruction change must be self-contained enough to evaluate and roll back.
awk '
function finish_entry() {
  if (!in_entry) return
  if (marker != 1) {
    printf "FAIL: fleet archive contract: instruction entry %s has %d FLEET-PROMPT-CHANGE markers\n", heading, marker > "/dev/stderr"
    exit 1
  }
  if (!automation || !evidence || !problem || !change || !invariants || !expected_effect || !rollback || !evaluation) {
    printf "FAIL: fleet archive contract: instruction entry %s is missing a required audit field\n", heading > "/dev/stderr"
    exit 1
  }
}
/^## / {
  finish_entry()
  in_entry = 1
  heading = substr($0, 4)
  marker = automation = evidence = problem = change = invariants = expected_effect = rollback = evaluation = 0
  next
}
in_entry && /^FLEET-PROMPT-CHANGE \| timestamp=/ { marker++; next }
in_entry && /^automation:/ { automation = 1; next }
in_entry && /^evidence:/ { evidence = 1; next }
in_entry && /^problem:/ { problem = 1; next }
in_entry && /^change:/ { change = 1; next }
in_entry && /^invariants:/ { invariants = 1; next }
in_entry && /^expected-effect:/ { expected_effect = 1; next }
in_entry && /^rollback:/ { rollback = 1; next }
in_entry && /^evaluation:/ { evaluation = 1; next }
END {
  finish_entry()
  if (!in_entry) {
    print "FAIL: fleet archive contract: instruction archive has no dated entries" > "/dev/stderr"
    exit 1
  }
}
' "${CHANGES}"

printf 'PASS: fleet archive structure, tracker wiring, and audit fields are deterministic\n'
