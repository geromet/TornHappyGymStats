#!/usr/bin/env bash
# s06-provenance-warnings.sh — Verify provenance warning workflow fields in local surfaces artifact.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly S05_VERIFY="${SCRIPT_DIR}/s05-local-surfaces.sh"
readonly LATEST_PATH="${ROOT_DIR}/web/data/surfaces/latest.json"

if [[ ! -x "${S05_VERIFY}" ]]; then
  chmod +x "${S05_VERIFY}"
fi

echo "==> Running baseline surfaces generation (S05 script)"
bash "${S05_VERIFY}"

echo "==> Verifying warnings payload shape in latest.json"
python3 - <<'PY' "${LATEST_PATH}"
import json, pathlib, sys

latest_path = pathlib.Path(sys.argv[1])
if not latest_path.exists() or latest_path.stat().st_size == 0:
    print(f"ERROR: missing or empty artifact: {latest_path}", file=sys.stderr)
    raise SystemExit(10)

with latest_path.open("r", encoding="utf-8") as f:
    payload = json.load(f)

warnings = payload.get("warnings", [])
if warnings is None:
    print("ERROR: warnings key exists but is null (expected array or absent)", file=sys.stderr)
    raise SystemExit(11)
if not isinstance(warnings, list):
    print("ERROR: warnings key must be an array when present", file=sys.stderr)
    raise SystemExit(11)

required_reason_fallback = "missing-provenance-record"
if len(warnings) == 0:
    print("OK: no warnings present; dashboard should render empty warning panel state.")
    raise SystemExit(0)

for idx, item in enumerate(warnings):
    if not isinstance(item, dict):
        print(f"ERROR: warnings[{idx}] is not an object", file=sys.stderr)
        raise SystemExit(12)
    reason = str(item.get("reasonCode", required_reason_fallback))
    if not reason:
        print(f"ERROR: warnings[{idx}] reasonCode empty", file=sys.stderr)
        raise SystemExit(13)

print(f"OK: warnings payload verified ({len(warnings)} warning records).")
PY

echo "==> S06 provenance warnings verification passed"
