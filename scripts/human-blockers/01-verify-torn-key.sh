#!/usr/bin/env bash
# 01-verify-torn-key.sh — confirm a live Torn API key is usable for the #104
# gates, without ever printing the key itself.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly ENV_FILE="${ROOT_DIR}/.env"

usage() {
  cat <<'EOF'
Usage: bash scripts/human-blockers/01-verify-torn-key.sh

Reads TORN_API_KEY from .env, makes one read-only GET /v2/user/basic call, and
reports the key's access level. The key value itself is never printed or
logged. Blocks #104's M010 gate (needs real roster stats) — see
docs/HUMAN-INPUT-QUEUE.md item 1.
EOF
}
[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { usage; exit 0; }

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "MISSING: ${ENV_FILE} does not exist." >&2
  echo "  Create it with a line: TORN_API_KEY=<your key>" >&2
  exit 1
fi

TORN_API_KEY="$(grep -E '^TORN_API_KEY=' "${ENV_FILE}" | head -1 | cut -d= -f2-)"

if [[ -z "${TORN_API_KEY}" ]]; then
  echo "MISSING: TORN_API_KEY is not set in ${ENV_FILE}." >&2
  echo "  Get a Limited-access key from https://www.torn.com/preferences.php#tab=api" >&2
  echo "  and add: TORN_API_KEY=<key>" >&2
  exit 1
fi

echo "==> calling GET /v2/user/basic (read-only, no game action)"
RESPONSE="$(curl -sS -m 15 "https://api.torn.com/v2/user/basic?key=${TORN_API_KEY}")"

if echo "${RESPONSE}" | grep -q '"error"'; then
  CODE="$(echo "${RESPONSE}" | grep -o '"code":[0-9]*' | head -1)"
  echo "FAIL: Torn API rejected the key (${CODE:-unknown error})." >&2
  echo "  Response: ${RESPONSE}" >&2
  echo "  Get a fresh key and update TORN_API_KEY in .env, then re-run this script." >&2
  exit 1
fi

PLAYER_ID="$(echo "${RESPONSE}" | grep -o '"player_id":[0-9]*' | head -1 | cut -d: -f2)"
echo "PASS: key is live, resolves to player_id=${PLAYER_ID:-unknown}"
echo
echo "This confirms the key can make a basic call. It does NOT confirm it has"
echo "faction/roster-stat access, which #104's M010 gate needs — that check"
echo "requires actually calling the faction endpoints the gate harness will use,"
echo "which doesn't exist as code yet (fleet work, tracked in #104)."
echo
echo "To report this to the fleet:"
cat <<EOF
  gh issue comment 104 --repo geromet/TornHappyGymStats --body "Confirmed a live Torn API key is available and working (player_id=${PLAYER_ID:-unknown}) as of \$(date -u +%Y-%m-%d). Unblocks the credential half of the M010 gate; the comparison harness itself still needs building."
EOF
