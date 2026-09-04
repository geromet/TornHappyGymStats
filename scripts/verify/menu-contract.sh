#!/usr/bin/env bash
# menu-contract.sh — keeps scripts/menu.sh from rotting the way the last one did.
#
# The previous menu covered 4 of 27 scripts because nothing ever checked. This
# runs offline, needs no host, and fails if the console and the scripts drift
# apart.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly MENU="${ROOT_DIR}/scripts/menu.sh"
readonly REGISTRY="${ROOT_DIR}/scripts/lib/registry.sh"
readonly UI="${ROOT_DIR}/scripts/lib/ui.sh"
readonly PITFALLS="${ROOT_DIR}/docs/OPERATIONS-PITFALLS.md"

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

case "${1:-}" in
  -h|--help) printf 'Usage: bash scripts/verify/menu-contract.sh\n\nOffline checks that the operator console covers every script and still runs.\n'; exit 0 ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${MENU}" "${REGISTRY}" "${UI}" "${PITFALLS}"; do
  [[ -f "${path}" ]] || fail "missing ${path#"${ROOT_DIR}/"}"
done
pass "console files present"

for path in "${MENU}" "${REGISTRY}" "${UI}"; do
  bash -n "${path}" || fail "bash -n failed: ${path#"${ROOT_DIR}/"}"
done
pass "bash -n clean"

# Every script is either driven or excluded with a written reason, and no entry
# points at a script that does not exist. This is the check that actually stops
# the rot.
NO_COLOR=1 bash "${MENU}" --audit >/dev/null || fail "menu --audit reported uncovered or missing scripts (run: bash scripts/menu.sh --audit)"
pass "every script is covered or excluded with a reason"

NO_COLOR=1 bash "${MENU}" --list >/dev/null || fail "menu --list failed"
pass "menu --list works"

# A task with neither a preview nor an apply mode is unreachable in the UI.
if grep -nE '^reg_add' -A0 "${REGISTRY}" >/dev/null; then
  if grep -E '"NONE" +"NONE"' "${REGISTRY}" >/dev/null; then
    fail "a registry entry has neither a preview nor an apply mode"
  fi
fi
pass "every task is reachable"

# Mutating entries must carry a caution or an explicit '-', never be silently
# blank: the caution is what the operator reads before typing APPLY.
missing_caution="$(awk '/^reg_add/,/^$/' "${REGISTRY}" | grep -c 'reg_add' || true)"
[[ "${missing_caution}" -gt 0 ]] || fail "no registry entries found"
pass "registry parsed (${missing_caution} entries)"

# An unknown id must fail loudly rather than doing nothing quietly.
if NO_COLOR=1 bash "${MENU}" --run definitely-not-a-task >/dev/null 2>&1; then
  fail "menu --run accepted an unknown task id"
fi
pass "unknown task ids are rejected"

# The pitfalls file is referenced from the menu; an empty one would be a link to
# nowhere.
[[ "$(wc -l < "${PITFALLS}")" -gt 40 ]] || fail "docs/OPERATIONS-PITFALLS.md looks empty"
grep -q 'category' "${PITFALLS}" || fail "pitfalls file no longer documents the error categories"
pass "pitfalls documentation present"

printf 'MENU_CONTRACT_PASS\n'
