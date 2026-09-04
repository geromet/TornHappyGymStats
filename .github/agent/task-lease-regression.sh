#!/usr/bin/env bash
# Synthetic stale-branch/dependency/parser controls for task-lease.sh.
set -euo pipefail
readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly VALIDATOR="${ROOT_DIR}/.github/agent/task-lease.sh"
for command_name in git jq mktemp base64 tr; do command -v "$command_name" >/dev/null || exit 2; done
readonly TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
readonly REPO="$TMP/repo" FIX="$TMP/fixtures" BIN="$TMP/bin"
mkdir -p "$REPO" "$FIX" "$BIN"
git -C "$REPO" init -q -b main
git -C "$REPO" config user.name fixture
git -C "$REPO" config user.email fixture@example.invalid
printf 'base\n' > "$REPO/file.txt"; git -C "$REPO" add .; git -C "$REPO" commit -q -m base
BASE="$(git -C "$REPO" rev-parse HEAD)"
git -C "$REPO" switch -q -c feat/lease; printf 'child\n' >> "$REPO/file.txt"; git -C "$REPO" commit -qam child
HEAD_SHA="$(git -C "$REPO" rev-parse HEAD)"
git -C "$REPO" switch -q main; printf 'parent\n' >> "$REPO/file.txt"; git -C "$REPO" commit -qam parent
PARENT_SHA="$(git -C "$REPO" rev-parse HEAD)"; git -C "$REPO" switch -q feat/lease

write_issue() {
  local number="$1" base="$2" branch="$3" depends="$4" state="$5" path="$6"
  local body
  body="$(cat <<EOF
### Base SHA

${base}

### Branch

${branch}

### Owner/session

fixture-session

### Scope

Synthetic task

### Non-scope

Everything else

### Depends on

${depends}

### Required evidence

T1

### State

${state}
EOF
)"
  jq -n --argjson number "$number" --arg body "$body" '{number:$number,state:"open",title:"[agent-task] fixture",labels:[{name:"area:agent-workflow"}],body:$body}' > "$path"
}
write_issue 1 "$BASE" feat/lease none Active "$FIX/issue-1.json"
write_issue 2 "$BASE" other/branch none Active "$FIX/issue-2.json"
printf '[]\n' > "$FIX/closed-prs.json"

rebuild_open_issues() {
  : > "$FIX/open-issues.b64"
  local file
  for file in "$@"; do base64 < "$file" | tr -d '\n' >> "$FIX/open-issues.b64"; printf '\n' >> "$FIX/open-issues.b64"; done
}
rebuild_open_issues "$FIX/issue-1.json"

cat > "$BIN/gh" <<'EOF'
#!/bin/sh
set -eu
: "${TASK_LEASE_FAKE_DIR:?}"
args="$*"
case "$args" in
  *"issues?state=open"*) cat "$TASK_LEASE_FAKE_DIR/open-issues.b64" ;;
  *"issues/1"*) cat "$TASK_LEASE_FAKE_DIR/issue-1.json" ;;
  *"issues/2"*) cat "$TASK_LEASE_FAKE_DIR/issue-2.json" ;;
  *"pulls?state=closed"*) cat "$TASK_LEASE_FAKE_DIR/closed-prs.json" ;;
  *"pulls/10"*) cat "$TASK_LEASE_FAKE_DIR/pr-10.json" ;;
  *) echo "unsupported fake gh call: $args" >&2; exit 64 ;;
esac
EOF
chmod +x "$BIN/gh"

failures=0
run_case() {
  local expected="$1" label="$2" needle="$3"; shift 3
  local output status
  set +e
  output="$(cd "$REPO" && PATH="$BIN:$PATH" TASK_LEASE_FAKE_DIR="$FIX" bash "$VALIDATOR" --repo test/repo "$@" 2>&1)"; status=$?
  set -e
  if [[ "$expected" == pass ]]; then
    if ((status==0)) && [[ "$output" == *TASK_LEASE_PASS* ]]; then echo "PASS: $label"; else echo "FAIL: $label\n$output" >&2; ((failures+=1)); fi
  elif ((status!=0)) && [[ "$output" == *"$needle"* ]]; then echo "PASS: $label"; else echo "FAIL: $label expected '$needle'\n$output" >&2; ((failures+=1)); fi
}

run_case pass "valid active lease" "" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
run_case fail "branch mismatch" "branch mismatch" --issue 1 --branch other --head "$HEAD_SHA" --target-base "$BASE"
run_case fail "stale target base" "refresh/rebase before handoff" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$PARENT_SHA"

write_issue 1 main feat/lease none Active "$FIX/issue-1.json"; rebuild_open_issues "$FIX/issue-1.json"
run_case fail "short/ref base is rejected" "full 40-hex" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
write_issue 1 "$BASE" feat/lease none Active "$FIX/issue-1.json"; rebuild_open_issues "$FIX/issue-1.json"

jq '.body += "\n### Branch\n\nfeat/lease\n"' "$FIX/issue-1.json" > "$FIX/tmp"; mv "$FIX/tmp" "$FIX/issue-1.json"; rebuild_open_issues "$FIX/issue-1.json"
run_case fail "duplicate generated heading rejected" "exactly once" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
write_issue 1 "$BASE" feat/lease none Active "$FIX/issue-1.json"; rebuild_open_issues "$FIX/issue-1.json"

cat > "$FIX/pr-body.md" <<'EOF'
<!-- hgs-evidence
lease: none
-->
Task lease: #1
EOF
run_case fail "conflicting lease declarations rejected" "conflicting lease declarations" --pr-body-file "$FIX/pr-body.md" --allow-no-lease --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"

write_issue 2 "$BASE" feat/lease none Active "$FIX/issue-2.json"; rebuild_open_issues "$FIX/issue-1.json" "$FIX/issue-2.json"
run_case fail "duplicate active branch lease" "duplicate active lease" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
write_issue 2 "$BASE" other/branch none Active "$FIX/issue-2.json"; rebuild_open_issues "$FIX/issue-1.json"

write_issue 1 "$BASE" feat/lease "PR #10" Active "$FIX/issue-1.json"; rebuild_open_issues "$FIX/issue-1.json"
jq -n --arg sha "$PARENT_SHA" '{number:10,state:"open",merged_at:null,merge_commit_sha:null,head:{sha:$sha}}' > "$FIX/pr-10.json"
run_case fail "open parent must be contained" "is open at" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
jq -n --arg sha "$PARENT_SHA" '{number:10,state:"closed",merged_at:null,merge_commit_sha:null,head:{sha:$sha}}' > "$FIX/pr-10.json"
run_case fail "closed-unmerged parent fails" "closed without merge" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"
jq -n --arg sha "$PARENT_SHA" '{number:10,state:"closed",merged_at:"2026-09-04T00:00:00Z",merge_commit_sha:$sha,head:{sha:$sha}}' > "$FIX/pr-10.json"
run_case fail "merged parent not refreshed into child" "merged at" --issue 1 --branch feat/lease --head "$HEAD_SHA" --target-base "$BASE"

if ((failures>0)); then echo "TASK_LEASE_REGRESSION_FAIL failures=$failures" >&2; exit 1; fi
echo TASK_LEASE_REGRESSION_PASS
