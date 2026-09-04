#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
LEASE_SCRIPT="${ROOT_DIR}/scripts/verify/task-lease.sh"
[[ -x "$LEASE_SCRIPT" || -f "$LEASE_SCRIPT" ]] || { echo "ERROR: missing $LEASE_SCRIPT" >&2; exit 2; }
for cmd in git python3 grep; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "ERROR: required command unavailable: $cmd" >&2; exit 2; }
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
repo="$tmp/repo"
mkdir -p "$repo"
git -C "$repo" init -q
git -C "$repo" config user.email task-lease@example.invalid
git -C "$repo" config user.name task-lease-test
printf 'base\n' > "$repo/base.txt"
git -C "$repo" add base.txt
git -C "$repo" commit -qm base
base_sha="$(git -C "$repo" rev-parse HEAD)"
git -C "$repo" switch -qc feat/task-62
printf 'work\n' > "$repo/work.txt"
git -C "$repo" add work.txt
git -C "$repo" commit -qm work

write_body() {
  cat > "$tmp/body.md" <<BODY
### Base SHA
$1

### Branch
$2

### Owner/session
automation-regression

### Scope
test

### Non-scope
none

### Depends on
${3:-none}

### Required evidence
T1

### State
${4:-active}
BODY
}
printf '[{"number":62,"body":""}]\n' > "$tmp/issues.json"
printf '[]\n' > "$tmp/prs.json"
mkdir -p "$tmp/deps"

run_lease() {
  (cd "$repo" && \
    TASK_LEASE_ISSUE_BODY_FILE="$tmp/body.md" \
    TASK_LEASE_ISSUES_JSON_FILE="$tmp/issues.json" \
    TASK_LEASE_PRS_JSON_FILE="$tmp/prs.json" \
    TASK_LEASE_DEPENDENCY_DIR="$tmp/deps" \
    TASK_LEASE_BASE_REF=refs/heads/master \
    bash "$LEASE_SCRIPT" 62 "$@")
}

write_body "$base_sha" feat/task-62 none active
run_lease --handoff | grep -q '^PASS: task lease #62 owns feat/task-62'

write_body "$base_sha" feat/wrong none active
if run_lease >"$tmp/out" 2>&1; then
  echo 'FAIL: wrong branch was accepted' >&2
  exit 1
fi
grep -q "does not match lease branch" "$tmp/out"

write_body "$base_sha" feat/task-62 none active
python3 - "$tmp/issues.json" <<'PY'
import json,sys
body='''### Base SHA\nabc\n\n### Branch\nfeat/task-62\n\n### State\nactive\n'''
json.dump([{"number":62,"body":""},{"number":99,"body":body}], open(sys.argv[1],'w'))
PY
if run_lease >"$tmp/out" 2>&1; then
  echo 'FAIL: duplicate lease was accepted' >&2
  exit 1
fi
grep -q "another active lease" "$tmp/out"
printf '[{"number":62,"body":""}]\n' > "$tmp/issues.json"

git -C "$repo" switch -q master
printf 'dependency\n' > "$repo/dependency.txt"
git -C "$repo" add dependency.txt
git -C "$repo" commit -qm dependency
dep_sha="$(git -C "$repo" rev-parse HEAD)"
git -C "$repo" switch -q feat/task-62
printf '{"state":"MERGED","mergedAt":"2026-09-04T00:00:00Z","mergeCommit":{"oid":"%s"}}\n' "$dep_sha" > "$tmp/deps/123.json"
write_body "$base_sha" feat/task-62 '#123' active
if run_lease >"$tmp/out" 2>&1; then
  echo 'FAIL: unrefreshed merged dependency was accepted' >&2
  exit 1
fi
grep -q "merged after this task base" "$tmp/out"

git -C "$repo" merge -q --no-edit master
write_body "$base_sha" feat/task-62 '#123' active
run_lease | grep -q '^PASS:'

old_head="$(git -C "$repo" rev-parse HEAD)"
printf 'late\n' > "$repo/late.txt"
git -C "$repo" add late.txt
git -C "$repo" commit -qm late
printf '[{"number":200,"state":"MERGED","headRefOid":"%s","mergedAt":"2026-09-04T00:00:00Z"}]\n' "$old_head" > "$tmp/prs.json"
if run_lease --handoff >"$tmp/out" 2>&1; then
  echo 'FAIL: post-merge commit was accepted' >&2
  exit 1
fi
grep -q "commits exist after task PR completion" "$tmp/out"

printf 'PASS: task-lease regression covers ownership, duplicate leases, dependency refresh, and post-PR commits\n'
