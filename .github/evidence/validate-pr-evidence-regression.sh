#!/usr/bin/env bash
# Negative/positive controls for the PR evidence parser and classifier handoff.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly VALIDATOR="${ROOT_DIR}/.github/evidence/validate-pr-evidence.sh"
readonly TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT
failures=0

write_body() {
  local path="$1" required="$2" observed="$3" unverified="$4" t2="$5" t3="$6" t4="$7"
  cat > "${path}" <<EOF
Test PR body.

<!-- hgs-evidence
task: #77
lease: none
required: ${required}
observed: ${observed}
unverified: ${unverified}
regression: synthetic evidence-contract negative control
tier2: ${t2}
tier3: ${t3}
tier4: ${t4}
-->
EOF
}

expect_pass() {
  local name="$1" body="$2"
  shift 2
  if bash "${VALIDATOR}" --body-file "${body}" --files "$@" >"${TMP_DIR}/out" 2>"${TMP_DIR}/err"; then
    printf 'PASS: %s\n' "${name}"
  else
    printf 'FAIL: %s unexpectedly failed\n' "${name}" >&2
    cat "${TMP_DIR}/err" >&2
    ((failures += 1))
  fi
}

expect_fail() {
  local name="$1" needle="$2" body="$3"
  shift 3
  if bash "${VALIDATOR}" --body-file "${body}" --files "$@" >"${TMP_DIR}/out" 2>"${TMP_DIR}/err"; then
    printf 'FAIL: %s unexpectedly passed\n' "${name}" >&2
    ((failures += 1))
    return
  fi
  if grep -Fq "${needle}" "${TMP_DIR}/err"; then
    printf 'PASS: %s\n' "${name}"
  else
    printf 'FAIL: %s failed for the wrong reason\n' "${name}" >&2
    cat "${TMP_DIR}/err" >&2
    ((failures += 1))
  fi
}

body="${TMP_DIR}/body.md"
write_body "${body}" T1 T1 none n/a n/a n/a
expect_pass "ordinary source T1 is complete" "${body}" src/HappyGymStats.Core/Foo.cs

write_body "${body}" T1 T1 none n/a n/a n/a
expect_fail "stronger diff cannot claim T1" "required evidence declaration disagrees" "${body}" \
  src/HappyGymStats.Data/Queries/PostgresQuery.cs

write_body "${body}" T2 T1 T2 "pending screenshot inspection after CI" n/a n/a
expect_pass "required visual proof may be honestly unverified" "${body}" \
  src/HappyGymStats.Blazor/Pages/War.razor

write_body "${body}" T2 T1 none "390/768/1440 screenshots" n/a n/a
expect_fail "missing observed proof must be declared unverified" "neither observed nor explicitly unverified" "${body}" \
  src/HappyGymStats.Blazor/Pages/War.razor

write_body "${body}" T4 T4 none n/a n/a n/a
expect_fail "required stronger tier needs concrete detail" "detail field may not be n/a" "${body}" \
  src/HappyGymStats.Data/Queries/PostgresQuery.cs

write_body "${body}" 'T2,T3,T4' 'T2,T4' T3 \
  "screenshots inspected" "operator dry-run pending" "postgres integration: 3 passed, 0 skipped"
expect_pass "mixed stronger tiers preserve union and explicit gap" "${body}" \
  src/HappyGymStats.Blazor/Pages/War.razor \
  scripts/deploy-backend.sh \
  src/HappyGymStats.Data/Migrations/Example.cs

cat > "${body}" <<'EOF'
No evidence block here.
EOF
expect_fail "missing block fails" "exactly one complete" "${body}" src/HappyGymStats.Core/Foo.cs

write_body "${body}" T1 T1 none n/a n/a n/a
cat >> "${body}" <<'EOF'
<!-- hgs-evidence
task: #77
lease: none
required: T1
observed: T1
unverified: none
regression: duplicate block
tier2: n/a
tier3: n/a
tier4: n/a
-->
EOF
expect_fail "duplicate block fails" "exactly one complete" "${body}" src/HappyGymStats.Core/Foo.cs

if (( failures > 0 )); then
  printf 'PR_EVIDENCE_REGRESSION_FAIL failures=%d\n' "${failures}" >&2
  exit 1
fi

echo "PR_EVIDENCE_REGRESSION_PASS"
