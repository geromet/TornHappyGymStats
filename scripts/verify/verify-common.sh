#!/usr/bin/env bash
# verify-common.sh — fail-closed primitives shared by the repository's verifiers.
#
# This file is sourced. It deliberately sets no shell options: callers own those,
# and every verifier here already runs under `set -euo pipefail`.
#
# WHY THIS EXISTS
#
# A verifier that cannot run must never be indistinguishable from a verifier that
# found nothing wrong. That is not hypothetical here:
#
#     scripts/verify/no-raw-playerid-log-templates.sh: line 13: rg: command not found
#     PASS: no raw player-id log templates found in src/*.cs
#
# ripgrep is not installed on the GitHub runner. The check ran `rg` as the
# condition of an `if`, so exit 127 took the negative branch and it printed PASS
# having never opened a file. The privacy guardrail against logging raw Torn
# player IDs reported success in CI for as long as it ran unattended.
#
# Two exit codes, kept distinct on purpose:
#   1  the thing being checked is wrong          (a real finding)
#   2  the verifier could not run                (infrastructure failure)
# Collapsing them is what made the false green readable as success.
# shellcheck shell=bash

# The verifier itself is broken or unrunnable. Never used for a real finding.
verify_die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 2
}

# Preflight an external command. Call before the first assertion, not lazily.
verify_require_command() {
  local command_name="${1:?command name required}"
  command -v "${command_name}" >/dev/null 2>&1 \
    || verify_die "required command unavailable: ${command_name}"
}

verify_require_commands() {
  local command_name
  for command_name in "$@"; do verify_require_command "${command_name}"; done
}

# Preflight a fixture or source file the assertions read.
verify_require_file() {
  local path="${1:?path required}"
  [[ -f "${path}" ]] || verify_die "required file missing: ${path}"
}

# Preflight a directory AND that it is non-empty for the given glob, because a
# search over zero files matches nothing and looks exactly like a clean result.
verify_require_files_matching() {
  local directory="${1:?directory required}" pattern="${2:?pattern required}"
  local listing status=0 count
  [[ -d "${directory}" ]] || verify_die "required directory missing: ${directory}"
  # `|| status=$?` and not a bare assignment: under `set -e` a failing command
  # substitution aborts the caller before the exit status can be inspected, which
  # would turn "ripgrep is broken" into a silent death rather than a diagnosis.
  listing="$(rg --files --glob "${pattern}" "${directory}" 2>&1)" || status=$?
  (( status == 0 )) || verify_die "ripgrep failed (exit ${status}) listing '${pattern}' under ${directory}/: ${listing}"
  count="$(printf '%s\n' "${listing}" | grep -c . || true)"
  (( count > 0 )) || verify_die "no files matching '${pattern}' under ${directory}/ — this check cannot prove anything"
  printf '%s' "${count}"
}

# THE SAFE FORM OF "ASSERT THIS PATTERN IS ABSENT".
#
# The bug was never ripgrep. It was the shape of the assertion:
#
#     if rg 'bad' src; then fail; fi        # a broken rg means "clean"
#     rg -q 'bad' src && fail || true       # a broken rg means "clean"
#
# Both read a non-zero exit as an absence of matches, and a missing tool, an
# unreadable path or a bad regex all exit non-zero. Every other verifier in this
# repository happens to use `rg -q ... || fail`, where a broken rg fails the
# check — which is why only one script was ever wrong.
#
# This helper distinguishes the three outcomes ripgrep actually reports:
#   0  matches found     -> a real finding, exit 1 via the caller's message
#   1  no matches        -> pass
#   2+ ripgrep failed    -> verify_die, exit 2
verify_no_match() {
  local description="${1:?description required}"; shift
  local output status=0
  # See the note in verify_require_files_matching: a bare assignment would let
  # `set -e` abort on ripgrep's exit 1 (the ordinary "no matches" case) before
  # this function could report a pass.
  output="$(rg -n "$@" 2>&1)" || status=$?
  case "${status}" in
    0)
      printf '%s\n' "${output}" >&2
      printf 'FAIL: %s\n' "${description}" >&2
      exit 1
      ;;
    1) return 0 ;;
    *) verify_die "ripgrep failed (exit ${status}) while checking: ${description}. Output: ${output}" ;;
  esac
}
