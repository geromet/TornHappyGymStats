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
  --pr-body-file FILE     Read the canonical lease declaration from PR metadata.
  --allow-no-lease        Skip when no lease is declared (for non-agent/external PRs).
EOF
}

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
infra() { printf 'ERROR: %s\n' "$*" >&2; exit 2; }
trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
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
    *) infra "unknown argument: $1" ;;
  esac
done

for command_name in git gh jq base64 awk sed grep; do
  command -v "$command_name" >/dev/null 2>&1 || infra "required command unavailable: $command_name"
done

api() {
  local label="$1"; shift
  local output
  output="$(gh api "$@")" || infra "GitHub API unavailable while ${label}"
  printf '%s' "$output"
}

issue_field() {
  local heading="$1" body="$2" count
  count="$(grep -Fxc "### ${heading}" <<<"$body" || true)"
  [[ "$count" == "1" ]] || fail "lease issue must contain heading '### ${heading}' exactly once (found ${count})"
  awk -v wanted="### ${heading}" '
    $0 == wanted { found=1; next }
    found && /^### / { exit }
    found && NF { print; exit }
  ' <<<"$body"
}

normalize_lease_ref() {
  local raw
  raw="$(trim "$1")"
  [[ "$raw" == "none" ]] && { printf 'none'; return; }
  raw="${raw#\#}"
  [[ "$raw" =~ ^[0-9]+$ ]] || fail "invalid task lease reference '${raw}' (expected #N or none)"
  printf '%s' "$raw"
}

if [[ -n "$pr_body_file" ]]; then
  [[ -f "$pr_body_file" ]] || infra "PR body file missing: $pr_body_file"
  if [[ -z "$lease_issue" ]]; then
    mapfile -t evidence_leases < <(awk '
      /^<!--[[:space:]]+hgs-evidence[[:space:]]*$/ { in_block=1; next }
      in_block && /^-->[[:space:]]*$/ { in_block=0; next }
      in_block && /^lease:[[:space:]]*/ { sub(/^lease:[[:space:]]*/, ""); print }
    ' "$pr_body_file")
    mapfile -t prose_leases < <(sed -nE 's/^[Tt]ask lease:[[:space:]]*(#?[0-9]+|none)[[:space:]]*$/\1/p' "$pr_body_file")
    ((${#evidence_leases[@]} <= 1)) || fail "multiple hgs-evidence lease declarations found"
    ((${#prose_leases[@]} <= 1)) || fail "multiple Task lease declarations found"

    evidence_ref=""
    prose_ref=""
    ((${#evidence_leases[@]} == 0)) || evidence_ref="$(normalize_lease_ref "${evidence_leases[0]}")"
    ((${#prose_leases[@]} == 0)) || prose_ref="$(normalize_lease_ref "${prose_leases[0]}")"
    if [[ -n "$evidence_ref" && -n "$prose_ref" && "$evidence_ref" != "$prose_ref" ]]; then
      fail "conflicting lease declarations: hgs-evidence='${evidence_ref}' Task lease='${prose_ref}'"
    fi
    lease_issue="${evidence_ref:-$prose_ref}"
  fi
fi

lease_issue="$(normalize_lease_ref "${lease_issue:-none}")"
if [[ "$lease_issue" == "none" ]]; then
  if (( allow_no_lease )); then
    echo "TASK_LEASE_SKIP: PR declares no agent task lease"
    exit 0
  fi
  fail "no task lease issue declared"
fi

if [[ -z "$repo" ]]; then
  repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null)" || infra "cannot resolve repository"
fi
[[ "$repo" == */* ]] || infra "cannot resolve OWNER/REPO"
if [[ -z "$branch" ]]; then branch="$(git branch --show-current)"; fi
[[ -n "$branch" ]] || infra "cannot resolve current branch; pass --branch"
if [[ -z "$head_sha" ]]; then head_sha="$(git rev-parse HEAD)"; fi
head_sha="$(git rev-parse "${head_sha}^{commit}" 2>/dev/null)" || infra "head commit is unavailable locally"

issue_json="$(api "reading lease issue #${lease_issue}" "repos/${repo}/issues/${lease_issue}")"
[[ "$(jq -r 'has("pull_request")' <<<"$issue_json")" != "true" ]] || fail "#${lease_issue} is a pull request, not an agent-task lease issue"
[[ "$(jq -r '.state' <<<"$issue_json")" == "open" ]] || fail "lease issue #${lease_issue} is closed; active work requires an open lease"
[[ "$(jq -r '.title // ""' <<<"$issue_json")" == '[agent-task] '* ]] || fail "lease issue #${lease_issue} is not an [agent-task] issue"
jq -e '.labels[]? | select((.name // .) == "area:agent-workflow")' <<<"$issue_json" >/dev/null \
  || fail "lease issue #${lease_issue} is missing area:agent-workflow label"
issue_body="$(jq -r '.body // ""' <<<"$issue_json")"

lease_base="$(trim "$(issue_field "Base SHA" "$issue_body")")"
lease_branch="$(trim "$(issue_field "Branch" "$issue_body")")"
lease_owner="$(trim "$(issue_field "Owner/session" "$issue_body")")"
lease_scope="$(trim "$(issue_field "Scope" "$issue_body")")"
lease_non_scope="$(trim "$(issue_field "Non-scope" "$issue_body")")"
lease_depends="$(trim "$(issue_field "Depends on" "$issue_body")")"
lease_evidence="$(trim "$(issue_field "Required evidence" "$issue_body")")"
lease_state="$(trim "$(issue_field "State" "$issue_body")")"

for required_name in lease_base lease_branch lease_owner lease_scope lease_non_scope lease_depends lease_evidence lease_state; do
  [[ -n "${!required_name}" && "${!required_name}" != "_No response_" ]] || fail "lease issue #${lease_issue} is missing required field ${required_name#lease_}"
done
[[ "$lease_base" =~ ^[0-9a-fA-F]{40}$ ]] || fail "lease Base SHA must be a full 40-hex commit SHA"
case "$lease_state" in Active|Blocked|Reopened) ;; *) fail "unsupported lease State='${lease_state}'" ;; esac
[[ "$branch" == "$lease_branch" ]] || fail "branch mismatch: lease owns '${lease_branch}' but current branch is '${branch}'"

lease_base_commit="$(git rev-parse "${lease_base}^{commit}" 2>/dev/null)" || infra "lease Base SHA ${lease_base} is unavailable locally; fetch history before handoff"
git merge-base --is-ancestor "$lease_base_commit" "$head_sha" || fail "lease base ${lease_base_commit} is not an ancestor of head ${head_sha}; refresh the lease after rebase/replacement"

if [[ -n "$target_base" ]]; then
  target_base_commit="$(git rev-parse "${target_base}^{commit}" 2>/dev/null)" || infra "target base ${target_base} is unavailable locally; fetch history before handoff"
  git merge-base --is-ancestor "$target_base_commit" "$head_sha" || fail "PR target/base ${target_base_commit} is not an ancestor of head ${head_sha}; refresh/rebase before handoff"
fi

owner="${repo%%/*}"
closed_prs="$(api "inspecting prior PRs for ${branch}" "repos/${repo}/pulls?state=closed&head=${owner}:${branch}&per_page=100")"
latest_closed="$(jq -c 'sort_by(.closed_at // "") | last // empty' <<<"$closed_prs")"
if [[ -n "$latest_closed" && "$lease_state" != "Reopened" ]]; then
  prior_number="$(jq -r '.number' <<<"$latest_closed")"
  prior_head="$(jq -r '.head.sha' <<<"$latest_closed")"
  [[ "$prior_head" != "$head_sha" ]] || fail "branch ${branch} already belongs to closed PR #${prior_number}; use a new task/branch or explicitly State=Reopened"
  fail "branch ${branch} received commits after closed PR #${prior_number}; follow-on work needs a new task/branch unless State=Reopened"
fi

open_leases_encoded="$(api "listing active lease issues" --paginate "repos/${repo}/issues?state=open&per_page=100" --jq '.[] | select(has("pull_request") | not) | @base64')"
while IFS= read -r encoded_issue; do
  [[ -n "$encoded_issue" ]] || continue
  candidate_json="$(printf '%s' "$encoded_issue" | base64 --decode)" || infra "cannot decode lease issue payload"
  candidate_number="$(jq -r '.number' <<<"$candidate_json")"
  [[ "$candidate_number" != "$lease_issue" ]] || continue
  [[ "$(jq -r '.title // ""' <<<"$candidate_json")" == '[agent-task] '* ]] || continue
  jq -e '.labels[]? | select((.name // .) == "area:agent-workflow")' <<<"$candidate_json" >/dev/null || continue
  candidate_body="$(jq -r '.body // ""' <<<"$candidate_json")"
  candidate_branch="$(trim "$(issue_field "Branch" "$candidate_body")")"
  [[ "$candidate_branch" != "$branch" ]] || fail "duplicate active lease: issues #${lease_issue} and #${candidate_number} both own branch ${branch}"
done <<<"$open_leases_encoded"

ensure_commit_fetched() {
  local sha="$1" label="$2"
  if ! git rev-parse "${sha}^{commit}" >/dev/null 2>&1; then
    git fetch --quiet origin "$sha" || infra "cannot fetch ${label} commit ${sha}"
  fi
  git rev-parse "${sha}^{commit}" 2>/dev/null || infra "${label} commit ${sha} is unavailable locally"
}

if [[ "$lease_depends" != "none" ]]; then
  IFS=',' read -r -a dependencies <<<"$lease_depends"
  for raw_dependency in "${dependencies[@]}"; do
    dependency="$(trim "$raw_dependency")"
    case "$dependency" in
      [Pp][Rr][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "$parent_number" =~ ^[0-9]+$ ]] || fail "malformed dependency '${dependency}'"
        parent_json="$(api "reading dependency PR #${parent_number}" "repos/${repo}/pulls/${parent_number}")"
        parent_state="$(jq -r '.state' <<<"$parent_json")"
        parent_merged="$(jq -r '.merged_at // empty' <<<"$parent_json")"
        if [[ -n "$parent_merged" ]]; then
          parent_sha="$(jq -r '.merge_commit_sha' <<<"$parent_json")"
          parent_commit="$(ensure_commit_fetched "$parent_sha" "dependency PR #${parent_number} merge")"
          git merge-base --is-ancestor "$parent_commit" "$head_sha" || fail "dependency PR #${parent_number} merged at ${parent_commit} but child head does not contain it; refresh/rebase the child branch"
        elif [[ "$parent_state" == "open" ]]; then
          parent_sha="$(jq -r '.head.sha' <<<"$parent_json")"
          parent_commit="$(ensure_commit_fetched "$parent_sha" "open dependency PR #${parent_number}")"
          git merge-base --is-ancestor "$parent_commit" "$head_sha" || fail "dependency PR #${parent_number} is open at ${parent_commit}; child must contain the current parent head before independent handoff"
        else
          fail "dependency PR #${parent_number} is closed without merge; dependent work is invalid until dependency is replaced or removed"
        fi
        ;;
      [Ii][Ss][Ss][Uu][Ee][[:space:]]\#*)
        parent_number="${dependency##*#}"
        [[ "$parent_number" =~ ^[0-9]+$ ]] || fail "malformed dependency '${dependency}'"
        parent_issue_json="$(api "reading dependency issue #${parent_number}" "repos/${repo}/issues/${parent_number}")"
        printf '  dependency issue #%s state=%s\n' "$parent_number" "$(jq -r '.state' <<<"$parent_issue_json")"
        ;;
      branch:*)
        parent_branch="$(trim "${dependency#branch:}")"
        git fetch --quiet origin "$parent_branch" || infra "dependency branch ${parent_branch} cannot be fetched"
        parent_commit="$(git rev-parse "refs/remotes/origin/${parent_branch}^{commit}")"
        git merge-base --is-ancestor "$parent_commit" "$head_sha" || fail "dependency branch ${parent_branch} advanced to ${parent_commit}; refresh/rebase the child branch"
        ;;
      *) fail "unsupported dependency '${dependency}'; use PR #N, issue #N, branch:name, or none" ;;
    esac
  done
fi

printf 'TASK_LEASE_PASS issue=#%s branch=%s state=%s base=%s head=%s evidence=%s owner=%s\n' \
  "$lease_issue" "$branch" "$lease_state" "$lease_base_commit" "$head_sha" "$lease_evidence" "$lease_owner"
