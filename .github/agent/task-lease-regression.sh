#!/usr/bin/env bash
# Synthetic stale-branch/dependency controls for task-lease.sh.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly VALIDATOR="${ROOT_DIR}/.github/agent/task-lease.sh"
for command_name in git jq mktemp base64 tr; do
  command -v "${command_name}" >/dev/null 2>&1 || { echo "ERROR: missing ${command_name}" >&2; exit 2; }
done

readonly TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT
readonly REPO="${TMP}/repo"
readonly FIX="${TMP}/fixtures"
readonly BIN="${TMP}/bin"
mkdir -p "${REPO}" "${FIX}" "${BIN}"

git -C "${REPO}" init -q -b main
git -C "${REPO}" config user.name fixture
git -C "${REPO}" config user.email fixture@example.invalid
printf 'base\n' > "${REPO}/file.txt"
git -C "${REPO}" add file.txt
git -C "${REPO}" commit -q -m base
BASE="$(git -C "${REPO}" rev-parse HEAD)"
git -C "${REPO}" switch -q -c feat/lease
printf 'child\n' >> "${REPO}/file.txt"
git -C "${REPO}" commit -qam child
HEAD_SHA="$(git -C "${REPO}" rev-parse HEAD)"

# A parent/main commit the child does not yet contain.
git -C "${REPO}" switch -q main
printf 'parent\n' >> "${REPO}/file.txt"
git -C "${REPO}" commit -qam parent
PARENT_SHA="$(git -C "${REPO}" rev-parse HEAD)"
git -C "${REPO}" switch -q feat/lease

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
  jq -n --argjson number "${number}" --arg body "${body}" '{number:$number,state:"open",body:$body}' > "${path}"
}

write_issue 1 "${BASE}" feat/lease none Active "${FIX}/issue-1.json"
write_issue 2 "${BASE}" other/branch none Active "${FIX}/issue-2.json"
printf '[]\n' > "${FIX}/closed-prs.json"
jq -n --arg sha "${PARENT_SHA}" '{number:10,state:"closed",merged_at:"2026-09-04T00:00:00Z",merge_commit_sha:$sha,head:{sha:$sha}}' > "${FIX}/pr-10.json"

rebuild_open_issues() {
  : > "${FIX}/open-issues.b64"
  local file encoded
  for file in "$@"; do
    encoded="$(base64 < "${file}" | tr -d '\n')"
    printf '%s\n' "${encoded}" >> "${FIX}/open-issues.b64"
  done
}
rebuild_open_issues "${FIX}/issue-1.json"

cat > "${BIN}/gh" <<'EOF'
#!/bin/sh
set -eu
: "${TASK_LEASE_FAKE_DIR:?}"
args="$*"
case "$args" in
  *"issues?state=open"*) cat "${TASK_LEASE_FAKE_DIR}/open-issues.b64" ;;
  *"issues/1"*) cat "${TASK_LEASE_FAKE_DIR}/issue-1.json" ;;
  *"issues/2"*) cat "${TASK_LEASE_FAKE_DIR}/issue-2.json" ;;
  *"pulls?state=closed"*) cat "${TASK_LEASE_FAKE_DIR}/closed-prs.json" ;;
  *"pulls/10"*) cat "${TASK_LEASE_FAKE_DIR}/pr-10.json" ;;
  *) echo "fake gh: unsupported call: $args" >&2; exit 64 ;;
esac
EOF
chmod +x "${BIN}/gh"

failures=0
run_case() {
  local expected="$1" label="$2" needle="$3"
  shift 3
  local output status
  set +e
  output="$(cd "${REPO}" && PATH="${BIN}:${PATH}" TASK_LEASE_FAKE_DIR="${FIX}" \
    bash "${VALIDATOR}" --repo test/repo "$@" 2>&1)"
  status=$?
  set -e

  if [[ "${expected}" == pass ]]; then
    if (( status == 0 )) && [[ "${output}" == *"TASK_LEASE_PASS"* ]]; then
      printf 'PASS: %s\n' "${label}"
    else
      printf 'FAIL: %s unexpectedly failed\n%s\n' "${label}" "${output}" >&2
      ((failures += 1))
    fi
    return
  fi

  if (( status == 0 )); then
    printf 'FAIL: %s unexpectedly passed\n%s\n' "${label}" "${output}" >&2
    ((failures += 1))
  elif [[ "${output}" == *"${needle}"* ]]; then
    printf 'PASS: %s\n' "${label}"
  else
    printf 'FAIL: %s failed for wrong reason; expected %q\n%s\n' "${label}" "${needle}" "${output}" >&2
    ((failures += 1))
  fi
}

run_case pass "valid active lease" "" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"

run_case fail "branch mismatch" "branch mismatch" \
  --issue 1 --branch feat/other --head "${HEAD_SHA}" --target-base "${BASE}"

# The target branch advanced after the child forked.
run_case fail "stale target base" "refresh/rebase before handoff" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${PARENT_SHA}"

# Moving the lease's recorded base to a commit outside the child ancestry catches
# rewritten/replaced work even when the branch name stayed the same.
write_issue 1 "${PARENT_SHA}" feat/lease none Active "${FIX}/issue-1.json"
rebuild_open_issues "${FIX}/issue-1.json"
run_case fail "lease base no longer ancestor" "lease base" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"
write_issue 1 "${BASE}" feat/lease none Active "${FIX}/issue-1.json"
rebuild_open_issues "${FIX}/issue-1.json"

# A branch is not silently reused after its task PR closes.
jq -n --arg sha "${HEAD_SHA}" '[{number:9,state:"closed",closed_at:"2026-09-04T00:00:00Z",merged_at:null,head:{sha:$sha}}]' > "${FIX}/closed-prs.json"
run_case fail "closed PR owns the finished branch" "already belongs to closed PR #9" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"

# Explicit reopening is the one supported exception.
write_issue 1 "${BASE}" feat/lease none Reopened "${FIX}/issue-1.json"
rebuild_open_issues "${FIX}/issue-1.json"
run_case pass "explicitly reopened task may reuse its branch" "" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"
printf '[]\n' > "${FIX}/closed-prs.json"
write_issue 1 "${BASE}" feat/lease none Active "${FIX}/issue-1.json"

# Two open lease issues may not own the same branch.
write_issue 2 "${BASE}" feat/lease none Active "${FIX}/issue-2.json"
rebuild_open_issues "${FIX}/issue-1.json" "${FIX}/issue-2.json"
run_case fail "duplicate active branch lease" "duplicate active lease" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"
write_issue 2 "${BASE}" other/branch none Active "${FIX}/issue-2.json"
rebuild_open_issues "${FIX}/issue-1.json"

# A dependency that merged after the child forked must be incorporated before
# handoff; merely changing the issue/PR state is not enough.
write_issue 1 "${BASE}" feat/lease "PR #10" Active "${FIX}/issue-1.json"
rebuild_open_issues "${FIX}/issue-1.json"
run_case fail "merged parent not refreshed into child" "dependency PR #10 merged" \
  --issue 1 --branch feat/lease --head "${HEAD_SHA}" --target-base "${BASE}"

if (( failures > 0 )); then
  printf 'TASK_LEASE_REGRESSION_FAIL failures=%d\n' "${failures}" >&2
  exit 1
fi

echo "TASK_LEASE_REGRESSION_PASS"
