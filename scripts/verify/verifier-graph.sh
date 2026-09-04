#!/usr/bin/env bash
# verifier-graph.sh — validates the verifier graph without executing the verifiers.
#
# The anti-rot property: adding a verifier without deciding whether it belongs in
# the canonical gate makes CI fail, rather than silently changing what the gate
# proves. Deleting or renaming one fails too, instead of leaving a dead entry.
#
# scripts/lib/registry.sh is the OPERATOR CONSOLE registry and is deliberately
# not reused here. Both are lists; they answer different questions — "what can an
# operator run" versus "what does the merge gate prove" — and merging them would
# make one of those answers a side effect of the other.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

manifest="${HAPPYGYMSTATS_VERIFY_MANIFEST:-${SCRIPT_DIR}/manifest.tsv}"
verify_dir="${HAPPYGYMSTATS_VERIFY_DIRECTORY:-${SCRIPT_DIR}}"
repo_root="${HAPPYGYMSTATS_VERIFY_REPO_ROOT:-${ROOT_DIR}}"

fail() {
  printf 'FAIL: verifier graph: %s\n' "$*" >&2
  exit 1
}

[[ -f "${manifest}" ]] || fail "manifest missing: ${manifest}"
[[ -d "${verify_dir}" ]] || fail "verify directory missing: ${verify_dir}"

declare -A seen_ids=()
declare -A seen_paths=()
row=0

while IFS=$'\t' read -r id script tier gate dependencies exclusion_reason extra; do
  ((row += 1))

  if (( row == 1 )); then
    [[ "${id}" == "id" && "${script}" == "script" && "${tier}" == "tier" \
      && "${gate}" == "gate" && "${dependencies}" == "dependencies" \
      && "${exclusion_reason}" == "exclusion_reason" && -z "${extra:-}" ]] \
      || fail "invalid header; expected six tab-separated columns"
    continue
  fi

  [[ -n "${id}" ]] || fail "row ${row}: id is empty"
  [[ -n "${script}" ]] || fail "row ${row}: script is empty"
  [[ -n "${tier}" ]] || fail "row ${row}: tier is empty"
  [[ -n "${gate}" ]] || fail "row ${row}: gate is empty"
  [[ -n "${dependencies}" ]] || fail "row ${row}: dependencies is empty (use '-' for none)"
  [[ -z "${extra:-}" ]] || fail "row ${row}: expected exactly six columns"

  [[ -z "${seen_ids[${id}]+x}" ]] || fail "duplicate id '${id}'"
  [[ -z "${seen_paths[${script}]+x}" ]] || fail "duplicate script '${script}'"
  seen_ids["${id}"]=1
  seen_paths["${script}"]=1

  case "${gate}" in
    required)
      [[ "${exclusion_reason}" == "-" ]] \
        || fail "row ${row}: required verifier '${id}' must use exclusion_reason '-'"
      ;;
    excluded)
      [[ -n "${exclusion_reason}" && "${exclusion_reason}" != "-" ]] \
        || fail "row ${row}: excluded verifier '${id}' needs a concrete exclusion reason"
      ;;
    *)
      fail "row ${row}: gate must be 'required' or 'excluded', got '${gate}'"
      ;;
  esac

  [[ "${script}" == scripts/verify/*.sh ]] \
    || fail "row ${row}: script must live under scripts/verify/: ${script}"
  [[ -f "${repo_root}/${script}" ]] \
    || fail "stale manifest entry '${id}': ${script} does not exist"
done < "${manifest}"

(( row > 1 )) || fail "manifest contains no verifier rows"

shopt -s nullglob
for absolute_path in "${verify_dir}"/*.sh; do
  relative_path="scripts/verify/${absolute_path##*/}"
  [[ -n "${seen_paths[${relative_path}]+x}" ]] \
    || fail "unregistered verifier file: ${relative_path}"
done

printf 'PASS: verifier manifest is complete, unique, and self-auditing (%d entries)\n' "$((row - 1))"
