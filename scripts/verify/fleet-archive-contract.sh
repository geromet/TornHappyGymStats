#!/usr/bin/env bash
# fleet-archive-contract.sh — deterministic structural contract for the Git-versioned fleet archive.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly ARCHITECTURE="${ROOT_DIR}/docs/fleet/SELF-IMPROVING-FLEET.md"
readonly ACTIVITY_DIR="${ROOT_DIR}/docs/fleet/archive/activity"
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

validate_activity_archive() {
  local activity="$1"

  if grep -Fq 'FLEET-PROMPT-CHANGE |' "${activity}"; then
    fail "${activity#"${ROOT_DIR}/"} contains a prompt-change marker"
  fi

  awk '
function finish_entry() {
  if (!in_entry) return
  if (snapshot != 1) {
    printf "FAIL: fleet archive contract: activity entry %s has %d FLEET-SNAPSHOT markers\n", heading, snapshot > "/dev/stderr"
    failed = 1
  }
  if (!repos || !next_pressure || material_fields < 3) {
    printf "FAIL: fleet archive contract: activity entry %s must include repos, next-pressure, and at least three labeled material fields\n", heading > "/dev/stderr"
    failed = 1
  }
}
/^## / {
  finish_entry()
  in_entry = 1
  heading = substr($0, 4)
  snapshot = repos = next_pressure = material_fields = 0
  next
}
in_entry && /^FLEET-SNAPSHOT \| period=/ { snapshot++; next }
in_entry && /^repos:/ { repos = 1; material_fields++; next }
in_entry && /^next-pressure:/ { next_pressure = 1; material_fields++; next }
in_entry && /^[a-z][a-z0-9\/-]*:/ { material_fields++; next }
END {
  finish_entry()
  if (!in_entry) {
    print "FAIL: fleet archive contract: activity archive has no dated entries" > "/dev/stderr"
    failed = 1
  }
  exit failed
}
' "${activity}"
}

require_file "${ARCHITECTURE}"
require_file "${CHANGES}"
[[ -d "${ACTIVITY_DIR}" ]] || fail "missing activity archive directory: docs/fleet/archive/activity"

# Keep the durable files and live trackers wired together in the architecture contract.
require_literal "${ARCHITECTURE}" 'docs/fleet/archive/activity/YYYY-MM.md'
require_literal "${ARCHITECTURE}" 'docs/fleet/archive/instruction-changes.md'
require_literal "${ARCHITECTURE}" '#170'
require_literal "${ARCHITECTURE}" '#171'
require_literal "${ARCHITECTURE}" 'no fleet merge into a repository default branch'
require_literal "${ARCHITECTURE}" 'truthful evidence / no invented verification'

# Validate every monthly archive, not only the month in which this verifier was introduced.
# This keeps the executable contract aligned with the documented YYYY-MM.md topology.
shopt -s nullglob
activity_files=("${ACTIVITY_DIR}"/*.md)
(( ${#activity_files[@]} > 0 )) || fail "activity archive has no monthly files"
for activity in "${activity_files[@]}"; do
  filename="${activity##*/}"
  [[ "${filename}" =~ ^[0-9]{4}-(0[1-9]|1[0-2])\.md$ ]] \
    || fail "invalid monthly activity archive filename: ${filename}"
  validate_activity_archive "${activity}"
done

# Month-agnostic regression: the same validator must accept a valid future-month archive.
tmp_dir="$(mktemp -d)"
trap 'rm -rf "${tmp_dir}"' EXIT
future_activity="${tmp_dir}/2099-12.md"
cat >"${future_activity}" <<'EOF'
# Fleet Activity Archive — 2099-12

## 2099-12-01T00:00Z — future-month contract fixture

FLEET-SNAPSHOT | period=2099-12-01T00:00Z..2099-12-01T01:00Z
repos: fixture repository
shipped: fixture result
coordination: fixture coordination state
next-pressure: fixture next pressure
EOF
validate_activity_archive "${future_activity}"

# Activity snapshots and prompt-change records are intentionally separate durable streams.
if grep -Fq 'FLEET-SNAPSHOT |' "${CHANGES}"; then
  fail "instruction-change archive contains an activity snapshot marker"
fi

# Every instruction change must be self-contained enough to evaluate and roll back.
awk '
function finish_entry() {
  if (!in_entry) return
  if (marker != 1) {
    printf "FAIL: fleet archive contract: instruction entry %s has %d FLEET-PROMPT-CHANGE markers\n", heading, marker > "/dev/stderr"
    failed = 1
  }
  if (!automation || !evidence || !problem || !change || !invariants || !expected_effect || !rollback || !evaluation) {
    printf "FAIL: fleet archive contract: instruction entry %s is missing a required audit field\n", heading > "/dev/stderr"
    failed = 1
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
    failed = 1
  }
  exit failed
}
' "${CHANGES}"

printf 'PASS: fleet archive structure, all monthly activity files, tracker wiring, and audit fields are deterministic\n'
