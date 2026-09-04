#!/usr/bin/env bash
# task-lease.sh — validate one lightweight GitHub issue-backed task/branch lease.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  bash .github/agent/task-lease.sh --issue N [options]
  bash .github/agent/task-lease.sh --pr-body-file FILE --allow-no-lease [options]

Options:
  --repo OWNER/REPO       Defaults to GITHUB_REPOSITORY or `gh repo view`.
  --branch NAME           Defaults to GITHUB_HEAD_REF or current git branch.
  --head SHA              Defaults to HEAD.
  --target-base SHA/REF   Current PR target/base tip; must be in head ancestry.
  --issue N               Agent-task lease issue number.
  --pr-body-file FILE     Read `lease: #N` from hgs-evidence or `Task lease: #N`.
  --allow-no-lease        Skip when no lease is declared (for non-agent PRs).
EOF
}

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
infra() { printf 'ERROR: %s\n' "$*" >&2; exit 2; }

repo="${GITHUB_REPOSITORY:-}"
branch="${GITHUB_HEAD_REF:-}"
head_sha=""
target_base=""
lease_issue=""
pr_body_file=""
allow_no_lease=0

while (($#)); do
  case "$1" in
    --repo) repo="${2:?--repo requires OWNER/REPO}"; shift 2 ;;
    --branch) branch="${2:?--branch requires NAME}"; shift 2 ;;
    --head) head_sha="${2:?--head requires SHA}"; shift 2 ;;
    --target-base) target_base="${2:?--target-base requires SHA/REF}"; shift 2 ;;
    --issue) lease_issue="${2:?--issue requires N}"; shift 2 ;;
    --pr-body-file) pr_body_file="${2:?--pr-body-file requires FILE}"; shift 2 ;;
    --allow-no-lease) allow_no_lease=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) infra "unknown argument: $1" ;;
  esac
done

for command_name in git gh jq base64 awk sed grep head; do
  command -v "${command_name}" >/dev/null 2>&1 || infra "required command unavailable: ${command_name}"
done

api() {
  local label="$1"; shift
  local output
  output="$(gh api "$@")" || infra "GitHub API unavailable while ${label}"
  printf '%s' "${output}"
}

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "${value}"
}

issue_field() {
  local heading="$1" body="$2"
  awk -v wanted="### ${heading}" '
    $0 == wanted { found=1; next }
    found && /^### / { exit }
    found && NF { print; exit }
  ' <<<"${body}"
}

if [[ -n "${pr_body_file}" ]]; then
  [[ -f "${pr_body_file}" ]] || infra "PR body file missing: ${pr_body_file}"
  if [[ -z "${lease_issue}" ]]; then
    lease_issue="$(awk '
      /^<!--[[:space:]]+hgs-evidence[[:space:]]*$/ { in_block=1; next }
      in_block && /^-->[[:space:]]*$/ { in_block=0; next }
      in_block && /^lease:[[:space:]]*/ { sub(/^lease:[[:space:]]*/, ""); print; exit }
    ' "${pr_body_file}")"
    if [[ -z "${lease_issue}" ]]; then
      lease_issue="$(sed -nE 's/^[Tt]ask lease:[[:space:]]*#?([0-9]+)[[:space:]]*$/\1/p' "${pr_body_file}" | head -1)"
    fi
  fi
fi

lease_issue="$(trim "${lease_issue}")"
if [[ -z "${lease_issue}" || "${lease_issue}" == "none" ]]; then
  if (( allow_no_lease )); then
    echo "TASK_LEASE_SKIP: PR declares no agent task lease"
    exit 0
  fi
  fail "no task lease issue declared"
fi
lease_issue="${lease_issue#\#}"
[[ "${lease_issue}" =~ ^[0-9]+$ ]] || fail "invalid task lease reference '${lease_issue}' (expected #N)"

if [[ -z "${repo}" ]]; then
  repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null)" || infra "cannot resolve repository"
fi
[[ "${repo}" == */* ]] || infra "cannot resolve OWNER/REPO"

if [[ -z "${branch}" ]]; then branch="$(git branch --show-current)"; fi
[[ -n "${branch}" ]] || infra "cannot resolve current branch; pass --branch"
if [[ -z "${head_sha}" ]]; then head_sha="$(git rev-parse HEAD)"; fi
head_sha="$(git rev-parse "${head_sha}^{commit}" 2>/dev/null)" || infra "head commit is unavailable locally"

issue_json="$(api "reading lease issue #${lease_issue}" "repos/${repo}/issues/${lease_issue}")"
[[ "$(jq -r 'has("pull_request")' <<<"${issue_json}")" != "true" ]] || fail "#${lease_issue} is a pull request, not an agent-task lease issue"
[[ "$(jq -r '.state' <<<"${issue_json}")" == "open" ]] || fail "lease issue #${lease_issue} is closed; active work requires an open lease"
issue_body="$(jq -r '.body // ""' <<<"${issue_json}")"

lease_base="$(trim "$(issue_field "Base SHA" "${issue_body}")")"
lease_branch="$(trim "$(issue_field "Branch" "${issue_body}")")"
lease_owner="$(trim "$(issue_field "Owner/session" "${issue_body}")")"
lease_scope="$(trim "$(issue_field "Scope" "${issue_body}")")"
lease_non_scope="$(trim "$(issue_field "Non-scope" "${issue_body}")")"
lease_depends="$(trim "$(issue_field "Depends on" "${issue_body}")")"
lease_evidence="$(trim "$(issue_field "Required evidence" "${issue_body}")")"
lease_state="$(trim "$(issue_field "State" "${issue_body}")")"

for required_name in lease_base lease_branch lease_owner lease_scope lease_non_scope lease_depends lease_evidence lease_state; do
  [[ -n "${!required_name}" && "${!required_name}" != "_No response_" ]] \
    || fail "lease issue #${lease_issue} is missing required field ${required_name#lease_}"
done
case "${lease_state}" in Active|Blocked|Reopened) ;; *) fail "unsupported lease State='${lease_state}'" ;; esac
[[ "${branch}" == "${lease_branch}" ]] || fail "branch mismatch: lease owns '${lease_branch}' but current branch is '${branch}'"

lease_base_commit="$(git rev-parse "${lease_base}^{commit}" 2>/dev/null)" \
  || infra "lease Base SHA ${lease_base} is unavailable locally; fetch history before handoff"
git merge-base --is-ancestor "${lease_base_commit}" "${head_sha}" \
  || fail "lease base ${lease_base_commit} is not an ancestor of head ${head_sha}; refresh the lease after rebase/replacement"

if [[ -n "${target_base}" ]]; then
  target_base_commit="$(git rev-parse "${target_base}^{commit}" 2>/dev/null)" \
    || infra "target base ${target_base} is unavailable locally; fetch history before handoff"
  git merge-base --is-ancestor "${target_base_commit}" "${head_sha}" \
    || fail "PR target/base ${target_base_commit} is not an ancestor of head ${head_sha}; refresh/rebase before handoff"
fi

# A branch whose task PR closed is finished. Explicit State=Reopened is the only
# supported exception; normal follow-on work gets a new task and branch.
owner="${repo%%/*}"
closed_prs="$(api "inspecting prior PRs for ${branch}" "repos/${repo}/pulls?state=closed&head=${owner}:${branch}&per_page=100")"
latest_closed="$(jq -c 'sort_by(.closed_at // "") | last // empty' <<<"${closed_prs}")"
if [[ -n "${latest_closed}" && "${lease_state}" != "Reopened" ]]; then
  prior_number="$(jq -r '.number' <<<"${latest_closed}")"
  prior_head="$(jq -r '.head.sha' <<<"${latest_closed}")"
  if [[ "${prior_head}" == "${head_sha}" ]]; then
    fail "branch ${branch} already belongs to closed PR #${prior_number}; use a new task/branch or explicitly State=Reopened"
  fi
  fail "branch ${branch} received commits after closed PR #${prior_number}; follow-on work needs a new task/branch unless State=Reopened"
fi

# Capture the paginated result before inspecting it. A process-substitution here
# would hide gh's exit status and could turn an API outage into "no duplicates".
open_leases_encoded="$(api "listing active lease issues" --paginate "repos/${repo}/issues?state=open&per_page=100" --jq '.[] | select(has("pull_request") | not) | @base64')"
while IFS= read -r encoded_issue; do
  [[ -n "${encoded_issue}" ]] || continue
  candidate_json="$(printf '%s' "${encoded_issue}" | base64 --decode)" || infra "cannot decode lease issue payload"
  candidate_number="$(jq -r '.number' <<<"${candidate_json}")"
  [[ "${candidate_number}" != "${lease_issue}" ]] || continue
  candidate_body="$(jq -r '.body // ""' <<<"${candidate_json}")"
  candidate_branch="$(trim "$(issue_field "Branch" "${candidate_body}")")"
  [[ "${candidate_branch}" != "${branch}" ]] \
    || fail "duplicate active lease: issues #${lease_issue} and #${candidate_number} both own branch ${branch}"
done <<<"${open_leases_encoded}"

# Dependencies are deliberately small syntax, not a scheduler. PR dependencies
# have exact merge ancestry; issue dependencies expose state; branch dependencies
# must be fetched so ancestry can be proved.
if [[ "${lease_depends}" != "none" ]]; then
  IFS=',' read -r -a dependencies <<<"${lease_depends}"
  for raw_dependency in "${dependencies[@]}"; do
    dependency="$(trim "${raw_dependency}")"
    case "${dependency}" in
      [Pp][Rr][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "${parent_number}" =~ ^[0-9]+$ ]] || fail "malformed dependency '${dependency}'"
        parent_json="$(api "reading dependency PR #${parent_number}" "repos/${repo}/pulls/${parent_number}")"
        if [[ -n "$(jq -r '.merged_at // empty' <<<"${parent_json}")" ]]; then
          parent_merge_sha="$(jq -r '.merge_commit_sha' <<<"${parent_json}")"
          parent_merge_commit="$(git rev-parse "${parent_merge_sha}^{commit}" 2>/dev/null)" \
            || infra "dependency PR #${parent_number} merged at ${parent_merge_sha} but that commit is not fetched"
          git merge-base --is-ancestor "${parent_merge_commit}" "${head_sha}" \
            || fail "dependency PR #${parent_number} merged at ${parent_merge_commit} but child head does not contain it; refresh/rebase the child branch"
        fi
        ;;
      [Ii][Ss][Ss][Uu][Ee][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "${parent_number}" =~ ^[0-9]+$ ]] || fail "malformed dependency '${dependency}'"
        parent_issue_json="$(api "reading dependency issue #${parent_number}" "repos/${repo}/issues/${parent_number}")"
        printf '  dependency issue #%s state=%s\n' "${parent_number}" "$(jq -r '.state' <<<"${parent_issue_json}")"
        ;;
      branch:*)
        parent_branch="$(trim "${dependency#branch:}")"
        if git rev-parse --verify "refs/remotes/origin/${parent_branch}^{commit}" >/dev/null 2>&1; then
          parent_ref="refs/remotes/origin/${parent_branch}"
        elif git rev-parse --verify "${parent_branch}^{commit}" >/dev/null 2>&1; then
          parent_ref="${parent_branch}"
        else
          infra "dependency branch ${parent_branch} is not fetched; fetch it before handoff"
        fi
        parent_commit="$(git rev-parse "${parent_ref}^{commit}")"
        git merge-base --is-ancestor "${parent_commit}" "${head_sha}" \
          || fail "dependency branch ${parent_branch} advanced to ${parent_commit}; refresh/rebase the child branch"
        ;;
      *) fail "unsupported dependency '${dependency}'; use PR #N, issue #N, branch:name, or none" ;;
    esac
  done
fi

printf 'TASK_LEASE_PASS issue=#%s branch=%s state=%s base=%s head=%s evidence=%s owner=%s\n' \
  "${lease_issue}" "${branch}" "${lease_state}" "${lease_base_commit}" "${head_sha}" "${lease_evidence}" "${lease_owner}"
