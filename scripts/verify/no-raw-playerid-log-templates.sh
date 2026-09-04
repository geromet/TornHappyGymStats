#!/usr/bin/env bash
set -euo pipefail

# Fails if any structured log template in source still references raw Torn player ID placeholders.
# This is a guardrail to prevent regressions like: "... Torn player {TornPlayerId}".

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT_DIR"

echo "==> Checking for raw player-id log templates"

# THE SEARCH PATH IS NOT OPTIONAL. rg was previously invoked with globs but no
# path. ripgrep only searches the working directory when stdin is a terminal; with
# stdin a pipe, socket or /dev/null it reads STDIN instead. Run from CI or any
# non-interactive shell this check therefore searched an empty stream, found
# nothing, and passed without ever looking at the source. It hung for 18 minutes
# on a socket stdin, which is the only reason it was noticed.
#
# `--glob 'src/**'` did not save it: a glob filters what a search walks, it does
# not tell rg what to walk.
readonly SEARCH_PATH="src"

# The guard for the failure above: if the tree we are supposed to be scanning has
# no .cs files, something is wrong with the path, not with the code. Passing then
# would be the same vacuous green all over again.
cs_count="$(rg --files --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' "${SEARCH_PATH}" | wc -l)"
if (( cs_count == 0 )); then
  echo "FAIL: found no .cs files under ${SEARCH_PATH}/ — this check cannot prove anything." >&2
  exit 1
fi

# Focus on source code only. Ignore docs/history where historical text may exist.
if rg -n --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' 'Log(Information|Warning|Error|Debug|Trace)\(.*\{TornPlayerId\}|Torn player \{TornPlayerId\}' "${SEARCH_PATH}" ; then
  echo "FAIL: raw player-id log template detected." >&2
  exit 1
fi

echo "PASS: no raw player-id log templates found in ${cs_count} .cs files under ${SEARCH_PATH}/"
