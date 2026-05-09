#!/usr/bin/env bash
set -euo pipefail

# Fails if any structured log template in source still references raw Torn player ID placeholders.
# This is a guardrail to prevent regressions like: "... Torn player {TornPlayerId}".

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT_DIR"

echo "==> Checking for raw player-id log templates"

# Focus on source code only. Ignore docs/history where historical text may exist.
if rg -n --glob 'src/**' --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' 'Log(Information|Warning|Error|Debug|Trace)\(.*\{TornPlayerId\}|Torn player \{TornPlayerId\}' ; then
  echo "FAIL: raw player-id log template detected." >&2
  exit 1
fi

echo "PASS: no raw player-id log templates found in src/*.cs"
