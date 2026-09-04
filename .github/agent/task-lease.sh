#!/usr/bin/env bash
# Validate one lightweight GitHub issue-backed task/branch lease.
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
  --target-base SHA/REF   Current PR target/base tip; must be an ancestor of head.
  --issue N               Agent-task lease issue number.
  --pr-body-file FILE     Read `lease: #N` from an hgs-evidence block, or
                          `Task lease: #N` from ordinary PR prose.
  --allow-no-lease        With --pr-body-file, print SKIP instead of failing when
                          no lease is declared. Intended for non-agent/external PRs.
EOF
}

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
    *) printf 'ERROR: unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

for command_name in git gh jq base64 awk sed grep; do
  command -v "${command_name}" >/dev/null 2>&1 || {
    printf 'ERROR: required command unavailable: %s\n' "${command_name}" >&2
    exit 2
  }
done

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "${value}"
}

if [[ -n "${pr_body_file}" ]]; then
  [[ -f "${pr_body_file}" ]] || { printf 'ERROR: PR body file missing: %s\n' "${pr_body_file}" >&2; exit 2; }
  if [[ -z "${lease_issue}" ]]; then
    lease_issue="$(awk '
      /^<!--[[:space:]]+hgs-evidence[[:space:]]*$/ { in_block=1; next }
      in_block && /^-->[[:space:]]*$/ { in_block=0; next }
      in_block && /^lease:[[:space:]]*/ {
        sub(/^lease:[[:space:]]*/, ""); print; exit
      }
    ' "${pr_body_file}")"
    if [[ -z "${lease_issue}" ]]; then
      lease_issue="$(sed -nE 's/^[Tt]ask lease:[[:space:]]*#?([0-9]+)[[:space:]]*$/\1/p' "${pr_body_file}" | head -1)"
    fi
  fi
fi

lease_issue="$(trim "${lease_issue}")"
if [[ "${lease_issue}" == "none" || -z "${lease_issue}" ]]; then
  if (( allow_no_lease )); then
    echo "TASK_LEASE_SKIP: PR declares no agent task lease"
    exit 0
  fi
  echo "ERROR: no task lease issue declared" >&2
  exit 1
fi
lease_issue="${lease_issue#\#}"
[[ "${lease_issue}" =~ ^[0-9]+$ ]] || {
  printf 'ERROR: invalid task lease reference: %s (expected #N)\n' "${lease_issue}" >&2
  exit 1
}

if [[ -z "${repo}" ]]; then
  repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null || true)"
fi
[[ "${repo}" == */* ]] || { printf 'ERROR: cannot resolve OWNER/REPO\n' >&2; exit 2; }

if [[ -z "${branch}" ]]; then
  branch="$(git branch --show-current)"
fi
[[ -n "${branch}" ]] || { printf 'ERROR: cannot resolve current branch; pass --branch\n' >&2; exit 2; }

if [[ -z "${head_sha}" ]]; then
  head_sha="$(git rev-parse HEAD)"
fi
head_sha="$(git rev-parse "${head_sha}^{commit}")" || { printf 'ERROR: head commit is unavailable\n' >&2; exit 2; }

issue_json="$(gh api "repos/${repo}/issues/${lease_issue}")" || {
  printf 'ERROR: cannot read lease issue #%s\n' "${lease_issue}" >&2
  exit 2
}
if [[ "$(jq -r 'has("pull_request")' <<<"${issue_json}")" == "true" ]]; then
  printf 'ERROR: #%s is a pull request, not an agent-task lease issue\n' "${lease_issue}" >&2
  exit 1
fi
if [[ "$(jq -r '.state' <<<"${issue_json}")" != "open" ]]; then
  printf 'ERROR: lease issue #%s is closed; active work requires an open lease\n' "${lease_issue}" >&2
  exit 1
fi
issue_body="$(jq -r '.body // ""' <<<"${issue_json}")"

issue_field() {
  local heading="$1" body="$2"
  awk -v wanted="### ${heading}" '
    $0 == wanted { found=1; next }
    found && /^### / { exit }
    found && NF { print; exit }
  ' <<<"${body}"
}

lease_base="$(trim "$(issue_field "Base SHA" "${issue_body}")")"
lease_branch="$(trim "$(issue_field "Branch" "${issue_body}")")"
lease_owner="$(trim "$(issue_field "Owner/session" "${issue_body}")")"
lease_scope="$(trim "$(issue_field "Scope" "${issue_body}")")"
lease_non_scope="$(trim "$(issue_field "Non-scope" "${issue_body}")")"
lease_depends="$(trim "$(issue_field "Depends on" "${issue_body}")")"
lease_evidence="$(trim "$(issue_field "Required evidence" "${issue_body}")")"
lease_state="$(trim "$(issue_field "State" "${issue_body}")")"

for required_name in lease_base lease_branch lease_owner lease_scope lease_non_scope lease_depends lease_evidence lease_state; do
  [[ -n "${!required_name}" && "${!required_name}" != "_No response_" ]] || {
    printf 'ERROR: lease issue #%s is missing required field %s\n' "${lease_issue}" "${required_name#lease_}" >&2
    exit 1
  }
done
case "${lease_state}" in
  Active|Blocked|Reopened) ;;
  *) printf 'ERROR: lease issue #%s has unsupported State=%q\n' "${lease_issue}" "${lease_state}" >&2; exit 1 ;;
esac

if [[ "${branch}" != "${lease_branch}" ]]; then
  printf 'ERROR: branch mismatch: lease owns %q but current branch is %q\n' "${lease_branch}" "${branch}" >&2
  exit 1
fi

lease_base_commit="$(git rev-parse "${lease_base}^{commit}" 2>/dev/null)" || {
  printf 'ERROR: lease Base SHA %s is unavailable locally; fetch history before handoff\n' "${lease_base}" >&2
  exit 2
}
if ! git merge-base --is-ancestor "${lease_base_commit}" "${head_sha}"; then
  printf 'ERROR: lease base %s is not an ancestor of head %s; branch was rebased/replaced without refreshing the lease\n' \
    "${lease_base_commit}" "${head_sha}" >&2
  exit 1
fi

if [[ -n "${target_base}" ]]; then
  target_base_commit="$(git rev-parse "${target_base}^{commit}" 2>/dev/null)" || {
    printf 'ERROR: target base %s is unavailable locally; fetch history before handoff\n' "${target_base}" >&2
    exit 2
  }
  if ! git merge-base --is-ancestor "${target_base_commit}" "${head_sha}"; then
    printf 'ERROR: PR target/base %s is not an ancestor of head %s; refresh/rebase before handoff\n' \
      "${target_base_commit}" "${head_sha}" >&2
    exit 1
  fi
fi

# A branch with a closed PR is finished. Reusing it is only legal when the same
# task was explicitly reopened; normal follow-on work needs a fresh lease/branch.
owner="${repo%%/*}"
closed_prs="$(gh api "repos/${repo}/pulls?state=closed&head=${owner}:${branch}&per_page=100")" || {
  printf 'ERROR: cannot inspect prior PRs for branch %s\n' "${branch}" >&2
  exit 2
}
latest_closed="$(jq -c 'sort_by(.closed_at // "") | last // empty' <<<"${closed_prs}")"
if [[ -n "${latest_closed}" && "${lease_state}" != "Reopened" ]]; then
  prior_number="$(jq -r '.number' <<<"${latest_closed}")"
  prior_head="$(jq -r '.head.sha' <<<"${latest_closed}")"
  if [[ "${prior_head}" == "${head_sha}" ]]; then
    printf 'ERROR: branch %s already belongs to closed PR #%s; close the lease or explicitly mark State=Reopened\n' \
      "${branch}" "${prior_number}" >&2
  else
    printf 'ERROR: branch %s received commits after closed PR #%s; follow-on work needs a new task/branch unless State=Reopened\n' \
      "${branch}" "${prior_number}" >&2
  fi
  exit 1
fi

# Duplicate branch leases are checked from the open issue collection directly,
# not GitHub search, so a just-created lease cannot hide behind index lag.
while IFS= read -r encoded_issue; do
  [[ -n "${encoded_issue}" ]] || continue
  candidate_json="$(printf '%s' "${encoded_issue}" | base64 --decode)"
  candidate_number="$(jq -r '.number' <<<"${candidate_json}")"
  [[ "${candidate_number}" != "${lease_issue}" ]] || continue
  candidate_body="$(jq -r '.body // ""' <<<"${candidate_json}")"
  candidate_branch="$(trim "$(issue_field "Branch" "${candidate_body}")")"
  if [[ "${candidate_branch}" == "${branch}" ]]; then
    printf 'ERROR: duplicate active lease: issues #%s and #%s both own branch %s\n' \
      "${lease_issue}" "${candidate_number}" "${branch}" >&2
    exit 1
  fi
done < <(gh api --paginate "repos/${repo}/issues?state=open&per_page=100" --jq '.[] | select(has("pull_request") | not) | @base64')

# Named dependencies stay lightweight: PR ancestry can be proved exactly; issue
# dependencies are state-only; branch dependencies are checked when the ref is
# available locally. Multiple entries are comma-separated.
if [[ "${lease_depends}" != "none" ]]; then
  IFS=',' read -r -a dependencies <<<"${lease_depends}"
  for raw_dependency in "${dependencies[@]}"; do
    dependency="$(trim "${raw_dependency}")"
    case "${dependency}" in
      [Pp][Rr][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "${parent_number}" =~ ^[0-9]+$ ]] || { printf 'ERROR: malformed dependency %q\n' "${dependency}" >&2; exit 1; }
        parent_json="$(gh api "repos/${repo}/pulls/${parent_number}")" || { printf 'ERROR: cannot inspect dependency PR #%s\n' "${parent_number}" >&2; exit 2; }
        parent_merged="$(jq -r '.merged_at // empty' <<<"${parent_json}")"
        if [[ -n "${parent_merged}" ]]; then
          parent_merge_sha="$(jq -r '.merge_commit_sha' <<<"${parent_json}")"
          parent_merge_commit="$(git rev-parse "${parent_merge_sha}^{commit}" 2>/dev/null)" || {
            printf 'ERROR: dependency PR #%s merged but commit %s is not fetched; refresh history before handoff\n' "${parent_number}" "${parent_merge_sha}" >&2
            exit 2
          }
          if ! git merge-base --is-ancestor "${parent_merge_commit}" "${head_sha}"; then
            printf 'ERROR: dependency PR #%s merged at %s but child head does not contain it; refresh/rebase the child branch\n' \
              "${parent_number}" "${parent_merge_commit}" >&2
            exit 1
          fi
        fi
        ;;
      [Ii][Ss][Ss][Uu][Ee][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "${parent_number}" =~ ^[0-9]+$ ]] || { printf 'ERROR: malformed dependency %q\n' "${dependency}" >&2; exit 1; }
        parent_issue_json="$(gh api "repos/${repo}/issues/${parent_number}")" || { printf 'ERROR: cannot inspect dependency issue #%s\n' "${parent_number}" >&2; exit 2; }
        parent_state="$(jq -r '.state' <<<"${parent_issue_json}")"
        printf '  dependency issue #%s state=%s\n' "${parent_number}" "${parent_state}"
        ;;
      branch:*)
        parent_branch="${dependency#branch:}"
        parent_branch="$(trim "${parent_branch}")"
        parent_ref=""
        if git rev-parse --verify "refs/remotes/origin/${parent_branch}^{commit}" >/dev/null 2>&1; then
          parent_ref="refs/remotes/origin/${parent_branch}"
        elif git rev-parse --verify "${parent_branch}^{commit}" >/dev/null 2>&1; then
          parent_ref="${parent_branch}"
        fi
        if [[ -n "${parent_ref}" ]]; then
          parent_commit="$(git rev-parse "${parent_ref}^{commit}")"
          if ! git merge-base --is-ancestor "${parent_commit}" "${head_sha}"; then
            printf 'ERROR: dependency branch %s has commit %s not contained in child head; refresh/rebase\n' \
              "${parent_branch}" "${parent_commit}" >&2
            exit 1
          fi
        else
          printf 'WARN: dependency branch %s is not fetched; ancestry not checked locally\n' "${parent_branch}" >&2
        fi
        ;;
      *) printf 'ERROR: unsupported dependency %q; use PR #N, issue #N, branch:name, or none\n' "${dependency}" >&2; exit 1 ;;
    esac
  done
fi

printf 'TASK_LEASE_PASS issue=#%s branch=%s state=%s base=%s head=%s evidence=%s owner=%s\n' \
  "${lease_issue}" "${branch}" "${lease_state}" "${lease_base_commit}" "${head_sha}" "${lease_evidence}" "${lease_owner}"
