#!/usr/bin/env bash
# 02-branch-protection-setup.sh — prepares (and, with --apply, sends) the
# required-status-checks ruleset update #56 asks for. Repo-admin action; never
# runs the mutation without --apply.
set -euo pipefail

readonly REPO="geromet/TornHappyGymStats"
readonly RULESET_ID=15843258
readonly PREREQ_PR=125
APPLY=0

usage() {
  cat <<'EOF'
Usage: bash scripts/human-blockers/02-branch-protection-setup.sh [--apply]

Without --apply: resolves the current stable CI check-run context names from a
recent successful PR, backs up the current Main ruleset to /tmp, and prints
the exact PATCH body #56 specifies — but sends nothing.

With --apply: does all of the above, then actually sends the PATCH. This
mutates a shared repository policy (what can merge to main); only run --apply
once you've read the printed diff.

Refuses to run at all until PR #125 (the #57 stabilization this depends on) is
merged. See docs/HUMAN-INPUT-QUEUE.md item 2.
EOF
}

for arg in "$@"; do
  case "${arg}" in
    -h|--help) usage; exit 0 ;;
    --apply) APPLY=1 ;;
    *) echo "Unknown argument: ${arg}" >&2; usage; exit 1 ;;
  esac
done

echo "==> checking prerequisite: PR #${PREREQ_PR} merged"
PREREQ_STATE="$(gh pr view "${PREREQ_PR}" --repo "${REPO}" --json state -q .state 2>/dev/null || echo "UNKNOWN")"
if [[ "${PREREQ_STATE}" != "MERGED" ]]; then
  echo "REFUSED: PR #${PREREQ_PR} is '${PREREQ_STATE}', not MERGED." >&2
  echo "  #56 explicitly waits for #57 to stabilize first; #57 is PR #${PREREQ_PR}." >&2
  echo "  Re-run this script once it merges." >&2
  exit 1
fi
echo "    PR #${PREREQ_PR} is merged — prerequisite satisfied"

echo "==> resolving check-run contexts from a recent successful PR head"
LAST_SHA="$(gh pr list --repo "${REPO}" --state merged --limit 1 --json headRefOid -q '.[0].headRefOid' 2>/dev/null || true)"
if [[ -z "${LAST_SHA}" ]]; then
  echo "FAIL: could not find a recently merged PR to read check-runs from." >&2
  exit 1
fi
echo "    using commit ${LAST_SHA}"

readonly WANTED=("build, verify contracts, test" "dotnet format" "shellcheck" "postgres integration")
mapfile -t FOUND < <(gh api "repos/${REPO}/commits/${LAST_SHA}/check-runs" --jq '.check_runs[] | select(.conclusion=="success") | .name' 2>/dev/null | sort -u)

declare -a REQUIRED_CONTEXTS=()
for want in "${WANTED[@]}"; do
  for have in "${FOUND[@]}"; do
    if [[ "${want}" == "${have}" ]]; then
      REQUIRED_CONTEXTS+=("${want}")
      break
    fi
  done
done

if [[ "${#REQUIRED_CONTEXTS[@]}" -eq 0 ]]; then
  echo "FAIL: none of the expected check names were found green on ${LAST_SHA}." >&2
  echo "  Found instead: ${FOUND[*]:-none}" >&2
  exit 1
fi
echo "    required contexts resolved: ${REQUIRED_CONTEXTS[*]}"

readonly BACKUP_PATH="/tmp/hgs-main-ruleset-before-$(date -u +%Y%m%dT%H%M%SZ).json"
echo "==> backing up current ruleset to ${BACKUP_PATH}"
gh api "repos/${REPO}/rulesets/${RULESET_ID}" > "${BACKUP_PATH}"
echo "    saved (operator artifact — not committed)"

CONTEXTS_JSON="$(printf '%s\n' "${REQUIRED_CONTEXTS[@]}" | jq -R . | jq -s '[.[] | {context: .}]')"

PATCH_BODY="$(jq -n --argjson contexts "${CONTEXTS_JSON}" '{
  rules: [
    {type: "deletion"},
    {type: "non_fast_forward"},
    {type: "pull_request", parameters: {
      required_approving_review_count: 0,
      dismiss_stale_reviews_on_push: false,
      required_reviewers: [],
      require_code_owner_review: false,
      require_last_push_approval: false,
      required_review_thread_resolution: false,
      require_extra_approval_for_unattributed_changes: true,
      allowed_merge_methods: ["merge", "squash"]
    }},
    {type: "required_status_checks", parameters: {
      strict_required_status_checks_policy: true,
      required_status_checks: $contexts
    }}
  ]
}')"

echo
echo "==> PATCH body (preserves existing deletion/non_fast_forward/pull_request rules):"
echo "${PATCH_BODY}" | jq .

if [[ "${APPLY}" -eq 0 ]]; then
  echo
  echo "Not applied (no --apply). To send it:"
  echo "  bash scripts/human-blockers/02-branch-protection-setup.sh --apply"
  exit 0
fi

echo
echo "==> sending PATCH to repos/${REPO}/rulesets/${RULESET_ID}"
echo "${PATCH_BODY}" | gh api -X PUT "repos/${REPO}/rulesets/${RULESET_ID}" --input - > /tmp/hgs-main-ruleset-after.json

echo "==> fetch-back assertion"
gh api "repos/${REPO}/rulesets/${RULESET_ID}" --jq '{enforcement, rules}'

echo
echo "Now run #56's end-to-end negative control by hand (a throwaway PR that"
echo "deliberately fails one required check) before closing #56 — this script"
echo "only covers the ruleset configuration step."
echo
echo "To report this to the fleet:"
cat <<EOF
  gh issue comment 56 --repo ${REPO} --body "Ruleset ${RULESET_ID} now requires: ${REQUIRED_CONTEXTS[*]} (strict/up-to-date policy on). Backup at ${BACKUP_PATH} (local, not committed). Still need the end-to-end negative-control proof (§6 of the issue) before closing."
EOF
