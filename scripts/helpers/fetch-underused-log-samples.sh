#!/usr/bin/env bash
set -euo pipefail

# Fetch Torn log samples for selected underused log types into a SINGLE redacted JSON file.
# Usage:
#   bash scripts/helpers/fetch-underused-log-samples.sh
#   bash scripts/helpers/fetch-underused-log-samples.sh --api-key "..." --limit 100 --out ./torn-log-samples.json

API_KEY=""
LIMIT="100"
OUT_FILE="./torn-log-samples.json"
BASE_URL="https://api.torn.com/v2/user/log"

usage() {
  cat <<'EOF'
Usage:
  bash scripts/helpers/fetch-underused-log-samples.sh
  bash scripts/helpers/fetch-underused-log-samples.sh --api-key "<key>" [--limit 100] [--out ./torn-log-samples.json]

Options:
  --api-key   Torn API key (if omitted, script prompts interactively)
  --limit     Page size per request (default: 100)
  --out       Output JSON file path (single consolidated file)
  -h, --help  Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api-key) API_KEY="${2:-}"; shift 2 ;;
    --limit) LIMIT="${2:-}"; shift 2 ;;
    --out) OUT_FILE="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ -z "$API_KEY" ]]; then
  read -r -s -p "Enter Torn API key: " API_KEY
  echo
fi

if [[ -z "$API_KEY" ]]; then
  echo "ERROR: Missing API key." >&2
  exit 2
fi

if ! [[ "$LIMIT" =~ ^[0-9]+$ ]]; then
  echo "ERROR: --limit must be numeric." >&2
  exit 2
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required for single-file JSON output and redaction." >&2
  exit 2
fi

mkdir -p "$(dirname "$OUT_FILE")"

LOG_TYPES=(
  "5915|Property kick|personal"
  "5916|Property kick receive|personal"
  "5963|Education complete|personal"
  "2051|Item finish book|personal"
  "2052|Item finish book strength increase|personal"
  "2053|Item finish book speed increase|personal"
  "2054|Item finish book defense increase|personal"
  "2055|Item finish book dexterity increase|personal"
  "2056|Item finish book working stats increase|personal"
  "2057|Item finish book list capacity increase|personal"
  "2058|Item finish book merit reset|personal"
  "2059|Item finish book drug addiction removal|personal"
  "2120|Item use parachute|personal"
  "2130|Item use skateboard|personal"
  "2140|Item use boxing gloves|personal"
  "2150|Item use dumbbells|personal"
  "6215|Job promote|company"
  "6217|Job fired|company"
  "6260|Company quit|company"
  "6261|Company fire send|company"
  "6262|Company fire receive|company"
  "6267|Company rank change send|company"
  "6268|Company rank change receive|company"
  "6760|Faction tree upgrade set|faction"
  "6761|Faction tree upgrade unset|faction"
  "6762|Faction tree upgrade restore|faction"
  "6763|Faction tree upgrade unset entire branch|faction"
  "6764|Faction tree upgrade restore entire branch|faction"
  "6765|Faction tree branch select|faction"
  "6766|Faction tree war mode|faction"
  "6767|Faction tree optimize|faction"
  "6800|Faction create|faction"
  "6830|Faction change leader|faction"
  "6831|Faction change leader receive|faction"
  "6832|Faction change leader auto receive|faction"
  "6833|Faction change leader auto remove|faction"
  "6835|Faction change coleader|faction"
  "6836|Faction change coleader noone (legacy)|faction"
  "6837|Faction change coleader remove|faction"
  "6838|Faction change coleader receive|faction"
)

tmp_json="$(mktemp)"
printf '[]' > "$tmp_json"

echo "Building single output file: $OUT_FILE"

for row in "${LOG_TYPES[@]}"; do
  IFS='|' read -r id label scope <<< "$row"
  url="$BASE_URL?log=$id&limit=$LIMIT&key=$API_KEY"

  resp_file="$(mktemp)"
  if ! curl -fsS "$url" -o "$resp_file"; then
    echo "WARN log=$id request failed"
    jq --arg id "$id" --arg label "$label" --arg scope "$scope" '. + [{logTypeId: ($id|tonumber), label: $label, scope: $scope, status: "request_error", count: 0, response: null}]' "$tmp_json" > "$tmp_json.next" && mv "$tmp_json.next" "$tmp_json"
    rm -f "$resp_file"
    continue
  fi

  jq '
    def redact_key($k):
      ($k | ascii_downcase) as $x
      | ($x == "anonymousid"
         or $x == "tornid"
         or ($x | test("(^|_|-)(user|player|sender|target|opponent|member|attacker|defender)id(s)?$"))
        );
    def scrub:
      if type == "object" then
        with_entries(if redact_key(.key) then .value = "REDACTED" else .value |= scrub end)
      elif type == "array" then map(scrub)
      else . end;
    scrub
  ' "$resp_file" > "$resp_file.redacted"

  count="$(jq -r 'if (.log|type)=="array" then (.log|length) else 0 end' "$resp_file.redacted")"

  jq --arg id "$id" --arg label "$label" --arg scope "$scope" --argjson c "$count" --slurpfile r "$resp_file.redacted" '. + [{logTypeId: ($id|tonumber), label: $label, scope: $scope, status: "ok", count: $c, response: $r[0]}]' "$tmp_json" > "$tmp_json.next" && mv "$tmp_json.next" "$tmp_json"

  echo "OK log=$id scope=$scope count=$count"
  rm -f "$resp_file" "$resp_file.redacted"
done

jq --arg generatedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" --arg limit "$LIMIT" '{generatedAt: $generatedAt, limit: ($limit|tonumber), source: "torn-v2-user-log", entries: .}' "$tmp_json" > "$OUT_FILE"
rm -f "$tmp_json"

echo "Done: $OUT_FILE"
jq -r '.entries[] | "log=\(.logTypeId) scope=\(.scope) status=\(.status) count=\(.count)"' "$OUT_FILE"
