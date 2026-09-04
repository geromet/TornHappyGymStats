#!/usr/bin/env bash
# 03-deploy-checklist.sh — prints the deploy runbook for merged runtime
# changes. Never runs a deploy/remote-exec script itself: per
# docs/WORKING-AGREEMENT.md §5, only the human runs those.
set -euo pipefail

readonly REPO="geromet/TornHappyGymStats"

usage() {
  cat <<'EOF'
Usage: bash scripts/human-blockers/03-deploy-checklist.sh

Prints which recently merged PRs changed rendered/runtime behavior (and so are
worth deploying), and the exact commands to run yourself. Does not deploy
anything. Optional / not fleet-blocking — see docs/HUMAN-INPUT-QUEUE.md item 3.
EOF
}
[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { usage; exit 0; }

echo "==> recently merged PRs (last 10)"
gh pr list --repo "${REPO}" --state merged --limit 10 \
  --json number,title,mergedAt -q '.[] | "  #\(.number)  \(.mergedAt)  \(.title)"'

echo
echo "This script cannot tell what's actually live on the server — that needs"
echo "SSH, which is yours to run, not this session's. Judge from the titles"
echo "above (docs/CI-only PRs don't need a deploy; Blazor/API behavior changes do)."
echo
echo "Recommended, in order, in your own terminal:"
cat <<'EOF'

  1. Dry run (default — no DEPLOY_*=1, no --confirm-*):
       ! bash scripts/menu.sh

     Pick the frontend (and/or backend) deploy task from the list.

  2. Review the dry-run output, then apply for real by supplying the gate:
       ! bash scripts/menu.sh
     ...selecting the same task with its confirm flag, exactly as menu.sh
     prompts for. menu.sh supplies DEPLOY_*=1 and --confirm-* for you; it is
     not a bypass and this script does not either.

  3. Post-deploy smoke check:
       ! DEPLOY_RUN_SMOKE=1 bash scripts/menu.sh
     or the smoke task directly, in remote mode.

EOF

echo "To report a completed deploy to the fleet (fill in what you actually ran):"
cat <<EOF
  gh issue comment <relevant PR or issue> --repo ${REPO} --body "Deployed \$(git rev-parse --short HEAD) to production on \$(date -u +%Y-%m-%d); smoke check <passed/failed>."
EOF
