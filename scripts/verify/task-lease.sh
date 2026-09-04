#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: bash scripts/verify/task-lease.sh ISSUE_NUMBER [--handoff]

Validates the current branch against an open agent-task issue lease. By default it
checks branch/base/dependency ownership. --handoff additionally requires the branch
to contain the current base ref and rejects work pushed after its PR was merged/closed.

Test seams:
  TASK_LEASE_ISSUE_BODY_FILE   read the selected issue body from this file
  TASK_LEASE_ISSUES_JSON_FILE  read open agent-task issues JSON from this file
  TASK_LEASE_PRS_JSON_FILE     read branch PR JSON from this file
  TASK_LEASE_DEPENDENCY_DIR    read PR dependency JSON from <dir>/<number>.json
  TASK_LEASE_BASE_REF          base ref used for freshness checks (default origin/main)
USAGE
}

die() { printf 'ERROR: %s\n' "$*" >&2; exit 2; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

[[ $# -ge 1 && $# -le 2 ]] || { usage >&2; exit 2; }
[[ "${1}" =~ ^[0-9]+$ ]] || die "issue number must be numeric"
issue_number="$1"
handoff=false
if [[ $# -eq 2 ]]; then
  [[ "$2" == "--handoff" ]] || die "unknown argument: $2"
  handoff=true
fi

for cmd in git awk sed grep python3; do
  command -v "$cmd" >/dev/null 2>&1 || die "required command unavailable: $cmd"
done
repo_root="$(git rev-parse --show-toplevel 2>/dev/null)" || die "not inside a git repository"
cd "$repo_root"
branch="$(git symbolic-ref --quiet --short HEAD 2>/dev/null)" || die "detached HEAD has no task lease"
head_sha="$(git rev-parse HEAD)"

read_issue_body() {
  if [[ -n "${TASK_LEASE_ISSUE_BODY_FILE:-}" ]]; then
    cat "$TASK_LEASE_ISSUE_BODY_FILE"
    return
  fi
  command -v gh >/dev/null 2>&1 || die "gh is required unless TASK_LEASE_ISSUE_BODY_FILE is provided"
  gh issue view "$issue_number" --json body,state --jq 'select(.state == "OPEN") | .body'
}

field() {
  local heading="$1"
  awk -v heading="$heading" '
    $0 == "### " heading { found=1; next }
    found && /^### / { exit }
    found && NF { print; exit }
  '
}

body="$(read_issue_body)" || die "could not read issue #${issue_number}"
[[ -n "$body" ]] || fail "issue #${issue_number} is missing, closed, or has an empty body"
lease_base="$(printf '%s\n' "$body" | field 'Base SHA')"
lease_branch="$(printf '%s\n' "$body" | field 'Branch')"
depends_on="$(printf '%s\n' "$body" | field 'Depends on')"
lease_state="$(printf '%s\n' "$body" | field 'State')"
[[ -n "$lease_base" ]] || fail "lease is missing Base SHA"
[[ -n "$lease_branch" ]] || fail "lease is missing Branch"
[[ -n "$lease_state" ]] || fail "lease is missing State"
[[ "$lease_state" =~ ^(active|Active|ACTIVE|reopened|Reopened|REOPENED)$ ]] || fail "lease state is not active/reopened: ${lease_state}"
[[ "$branch" == "$lease_branch" ]] || fail "current branch '${branch}' does not match lease branch '${lease_branch}'"
git cat-file -e "${lease_base}^{commit}" 2>/dev/null || fail "lease Base SHA '${lease_base}' is not available locally"
git merge-base --is-ancestor "$lease_base" HEAD || fail "lease Base SHA ${lease_base} is not an ancestor of HEAD ${head_sha}"

base_ref="${TASK_LEASE_BASE_REF:-origin/main}"
if git rev-parse --verify --quiet "${base_ref}^{commit}" >/dev/null; then
  git merge-base --is-ancestor "$lease_base" "$base_ref" || fail "lease Base SHA ${lease_base} is not on ${base_ref}; refresh the task base"
  if $handoff; then
    git merge-base --is-ancestor "$base_ref" HEAD || fail "branch is stale against ${base_ref}; refresh/rebase before handoff"
  fi
fi

read_open_issues_json() {
  if [[ -n "${TASK_LEASE_ISSUES_JSON_FILE:-}" ]]; then
    cat "$TASK_LEASE_ISSUES_JSON_FILE"
    return
  fi
  command -v gh >/dev/null 2>&1 || die "gh is required unless TASK_LEASE_ISSUES_JSON_FILE is provided"
  gh issue list --label agent-task --state open --limit 100 --json number,body
}

issues_json="$(read_open_issues_json)" || die "could not list active agent-task issues"
duplicates="$(python3 -c 'import json,re,sys
current=int(sys.argv[1]); branch=sys.argv[2]
items=json.load(sys.stdin)
pat=re.compile(r"^### Branch\\s*\\n([^\\n]+)", re.M)
for item in items:
    if int(item.get("number", -1)) == current: continue
    m=pat.search(item.get("body") or "")
    if m and m.group(1).strip() == branch: print(item["number"])' "$issue_number" "$lease_branch" <<<"$issues_json")" || die "could not parse active task leases"
[[ -z "$duplicates" ]] || fail "branch '${lease_branch}' has another active lease: issue #${duplicates//$'\n'/, #}"

check_dependency_pr() {
  local pr="$1" json merged_at merge_sha state
  if [[ -n "${TASK_LEASE_DEPENDENCY_DIR:-}" ]]; then
    [[ -f "${TASK_LEASE_DEPENDENCY_DIR}/${pr}.json" ]] || return 0
    json="$(cat "${TASK_LEASE_DEPENDENCY_DIR}/${pr}.json")"
  else
    command -v gh >/dev/null 2>&1 || die "gh is required to inspect dependency PR #${pr}"
    json="$(gh pr view "$pr" --json state,mergedAt,mergeCommit)"
  fi
  read -r state merged_at merge_sha < <(python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("state") or "", d.get("mergedAt") or "", (d.get("mergeCommit") or {}).get("oid") or "")' <<<"$json")
  if [[ "$state" == "MERGED" || -n "$merged_at" ]]; then
    [[ -n "$merge_sha" ]] || fail "dependency PR #${pr} is merged but has no merge commit SHA"
    git cat-file -e "${merge_sha}^{commit}" 2>/dev/null || fail "dependency PR #${pr} merged at ${merge_sha}, but that commit is not available locally; fetch and refresh"
    git merge-base --is-ancestor "$merge_sha" HEAD || fail "dependency PR #${pr} merged after this task base; refresh/rebase before handoff"
  fi
}

if [[ -n "$depends_on" && ! "$depends_on" =~ ^([Nn]one|[Nn]/[Aa]|-)$ ]]; then
  while read -r pr; do
    check_dependency_pr "$pr"
  done < <(grep -Eo '#[0-9]+' <<<"$depends_on" | tr -d '#' | sort -u)
fi

if $handoff; then
  if [[ -n "${TASK_LEASE_PRS_JSON_FILE:-}" ]]; then
    prs_json="$(cat "$TASK_LEASE_PRS_JSON_FILE")"
  else
    command -v gh >/dev/null 2>&1 || die "gh is required unless TASK_LEASE_PRS_JSON_FILE is provided"
    prs_json="$(gh pr list --head "$lease_branch" --state all --limit 20 --json number,state,headRefOid,mergedAt)"
  fi
  stale_pr="$(python3 -c 'import json,sys
head=sys.argv[1]
prs=json.load(sys.stdin)
for pr in prs:
    state=(pr.get("state") or "").upper()
    if state in {"MERGED","CLOSED"} and pr.get("headRefOid") and pr["headRefOid"] != head:
        print(f"#{pr.get('"'"'number'"'"')} ({state.lower()}) recorded {pr['"'"'headRefOid'"'"']}, current HEAD is {head}")
        break' "$head_sha" <<<"$prs_json")" || die "could not parse branch pull requests"
  if [[ -n "$stale_pr" && ! "$lease_state" =~ ^(reopened|Reopened|REOPENED)$ ]]; then
    fail "commits exist after task PR completion: ${stale_pr}. Open a new task/branch or set the explicitly reopened task state to reopened."
  fi
fi

printf 'PASS: task lease #%s owns %s at %s\n' "$issue_number" "$branch" "$head_sha"
