#!/usr/bin/env bash
set -euo pipefail

# Fails if any structured log template in source still references raw Torn player ID placeholders.
# This is a guardrail to prevent regressions like: "... Torn player {TornPlayerId}".
#
# This is the check that was silently passing in CI while searching nothing, and
# it is now the worked example for verify-common.sh. See that file for the whole
# story; the short version is that `if rg ...; then FAIL; fi` reads a broken
# ripgrep as an absence of matches.

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands rg wc

readonly SEARCH_PATH="src"

echo "==> Checking for raw player-id log templates"

# The search path is not optional: ripgrep only walks the working directory when
# stdin is a terminal, and reads STDIN when given a pipe, socket or /dev/null.
# A glob filters what a search walks; it does not tell rg what to walk.
cs_count="$(verify_require_files_matching "${SEARCH_PATH}" '*.cs')"

verify_no_match \
  "raw player-id log template detected" \
  --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' \
  'Log(Information|Warning|Error|Debug|Trace)\(.*\{TornPlayerId\}|Torn player \{TornPlayerId\}' \
  "${SEARCH_PATH}"

echo "PASS: no raw player-id log templates found in ${cs_count} .cs files under ${SEARCH_PATH}/"
