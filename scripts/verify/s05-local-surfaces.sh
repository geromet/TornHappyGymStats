#!/usr/bin/env bash
# s05-local-surfaces.sh — Generate and verify local surfaces artifacts for S05.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"
readonly SURFACES_DIR="${ROOT_DIR}/web/data/surfaces"
readonly META_PATH="${SURFACES_DIR}/meta.json"
readonly LATEST_PATH="${SURFACES_DIR}/latest.json"
readonly API_URL="http://127.0.0.1:5181"
readonly HEALTH_URL="${API_URL}/api/v1/torn/health"
readonly IMPORT_URL="${API_URL}/api/v1/torn/import-jobs"
readonly TIMEOUT_SECONDS="${S05_IMPORT_TIMEOUT_SECONDS:-180}"

usage() {
  cat <<EOF
Usage: bash scripts/verify/s05-local-surfaces.sh

Runs a deterministic local pre-frontend check for S05:
  1) starts the API locally
  2) validates API key env precondition
  3) enqueues an import job
  4) waits for web/data/surfaces/meta.json + latest.json
  5) validates required JSON keys: version, series.gymCloud

Required env:
  - TORN_API_KEY (preferred) or HAPPYGYMSTATS_TORN_API_KEY
Optional:
  - S05_IMPORT_TIMEOUT_SECONDS (default: 180)
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

api_key="${TORN_API_KEY:-${HAPPYGYMSTATS_TORN_API_KEY:-}}"
if [[ -z "${api_key}" ]]; then
  echo "ERROR: Missing API key env. Set TORN_API_KEY (or HAPPYGYMSTATS_TORN_API_KEY) and retry." >&2
  exit 2
fi

mkdir -p "${SURFACES_DIR}"

api_log="$(mktemp /tmp/hgs-s05-api.XXXX.log)"
cleanup() {
  if [[ -n "${api_pid:-}" ]] && kill -0 "${api_pid}" 2>/dev/null; then
    kill "${api_pid}" >/dev/null 2>&1 || true
    wait "${api_pid}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "==> Starting local API for S05 verification"
ASPNETCORE_URLS="${API_URL}" \
HAPPYGYMSTATS_SURFACES_CACHE_DIR="${SURFACES_DIR}" \
DOTNET_ENVIRONMENT="Development" \
dotnet run --no-launch-profile --project "${API_PROJECT}" >"${api_log}" 2>&1 &
api_pid=$!

echo "==> Waiting for API health (${HEALTH_URL})"
for _ in $(seq 1 60); do
  if curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
if ! curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
  echo "ERROR: API did not become healthy. Inspect logs at ${api_log}" >&2
  exit 3
fi

echo "==> Enqueueing import job"
import_response="$(curl -fsS -X POST "${IMPORT_URL}" -H 'Content-Type: application/json' -d "{\"apiKey\":\"${api_key}\",\"fresh\":false}")" || {
  echo "ERROR: Import job request failed. Inspect logs at ${api_log}" >&2
  exit 4
}

if ! python3 - <<'PY' "${import_response}"
import json, sys
try:
    payload = json.loads(sys.argv[1])
except json.JSONDecodeError as exc:
    print(f"ERROR: malformed import response: {exc}", file=sys.stderr)
    raise SystemExit(5)
if not isinstance(payload, dict) or "id" not in payload:
    print("ERROR: import response missing required key: id", file=sys.stderr)
    raise SystemExit(5)
PY
then
  exit 5
fi

echo "==> Waiting up to ${TIMEOUT_SECONDS}s for surfaces cache files"
start_epoch="$(date +%s)"
while true; do
  if [[ -s "${META_PATH}" && -s "${LATEST_PATH}" ]]; then
    break
  fi
  now_epoch="$(date +%s)"
  if (( now_epoch - start_epoch >= TIMEOUT_SECONDS )); then
    echo "ERROR: timeout waiting for ${META_PATH} and ${LATEST_PATH}. Inspect logs at ${api_log}" >&2
    exit 6
  fi
  sleep 2
done

echo "==> Validating JSON envelopes and required keys"
python3 - <<'PY' "${META_PATH}" "${LATEST_PATH}"
import json, pathlib, sys
meta_path = pathlib.Path(sys.argv[1])
latest_path = pathlib.Path(sys.argv[2])

for path in (meta_path, latest_path):
    if not path.exists() or path.stat().st_size == 0:
        print(f"ERROR: missing or empty artifact: {path}", file=sys.stderr)
        raise SystemExit(7)

with meta_path.open("r", encoding="utf-8") as f:
    meta = json.load(f)
with latest_path.open("r", encoding="utf-8") as f:
    latest = json.load(f)

if "version" not in meta and "currentVersion" not in meta:
    print("ERROR: meta.json missing required key: version/currentVersion", file=sys.stderr)
    raise SystemExit(8)
if "version" not in latest:
    print("ERROR: latest.json missing required key: version", file=sys.stderr)
    raise SystemExit(8)

series = latest.get("series")
if not isinstance(series, dict) or "gymCloud" not in series:
    print("ERROR: latest.json missing required key path: series.gymCloud", file=sys.stderr)
    raise SystemExit(8)

print("OK: surfaces artifacts are present and valid (version, series.gymCloud).")
PY

echo "==> S05 local surfaces verification passed"
