#!/usr/bin/env bash
# 00-run-all.sh — walk the human-input queue (docs/HUMAN-INPUT-QUEUE.md) in
# order, pausing between items. Picks up any new 0N-*.sh dropped in this
# directory without needing an edit here.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

usage() {
  cat <<'EOF'
Usage: bash scripts/human-blockers/00-run-all.sh

Runs each numbered script in this directory in order, pausing for you to read
the output (and, where relevant, decide whether to re-run with --apply) before
moving on. See docs/HUMAN-INPUT-QUEUE.md for what each item is and why it
needs a human.

Nothing here performs a T3 deploy/remote action itself.
EOF
}
[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { usage; exit 0; }

mapfile -t STEPS < <(find "${SCRIPT_DIR}" -maxdepth 1 -name '0[1-9]*.sh' | sort)

if [[ "${#STEPS[@]}" -eq 0 ]]; then
  echo "No queue items found in ${SCRIPT_DIR}." >&2
  exit 1
fi

for step in "${STEPS[@]}"; do
  echo
  echo "════════════════════════════════════════════════════════════════"
  echo "  $(basename "${step}")"
  echo "════════════════════════════════════════════════════════════════"
  if ! bash "${step}"; then
    echo
    echo "!! ${step} exited non-zero (a refusal or a real failure — read above)."
    read -r -p "Continue to the next item anyway? [y/N] " reply
    [[ "${reply}" =~ ^[Yy]$ ]] || { echo "Stopping."; exit 1; }
  fi
  read -r -p $'\nPress Enter to continue to the next item...' _
done

echo
echo "Done. Run the printed 'gh issue comment' commands you're satisfied with"
echo "to hand the results back to the fleet."
