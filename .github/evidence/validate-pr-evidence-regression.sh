#!/usr/bin/env bash
set -euo pipefail
readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly VALIDATOR="${ROOT_DIR}/.github/evidence/validate-pr-evidence.sh"
readonly TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT
failures=0
write_body() {
  local path="$1" required="$2" observed="$3" unverified="$4" security="$5" t2="$6" t3="$7" t4="$8"
  cat > "$path" <<EOF
Test PR body.
<!-- hgs-evidence
task: #77
lease: none
required: ${required}
observed: ${observed}
unverified: ${unverified}
regression: synthetic evidence-contract negative control
security-negative-control: ${security}
tier2: ${t2}
tier3: ${t3}
tier4: ${t4}
-->
EOF
}
expect_pass() { local name="$1" body="$2"; shift 2; if bash "$VALIDATOR" --body-file "$body" --files "$@" >/dev/null 2>"${TMP_DIR}/err"; then echo "PASS: $name"; else echo "FAIL: $name" >&2; cat "${TMP_DIR}/err" >&2; ((failures+=1)); fi; }
expect_fail() { local name="$1" needle="$2" body="$3"; shift 3; if bash "$VALIDATOR" --body-file "$body" --files "$@" >/dev/null 2>"${TMP_DIR}/err"; then echo "FAIL: $name unexpectedly passed" >&2; ((failures+=1)); elif grep -Fq "$needle" "${TMP_DIR}/err"; then echo "PASS: $name"; else echo "FAIL: $name wrong reason" >&2; cat "${TMP_DIR}/err" >&2; ((failures+=1)); fi; }
body="${TMP_DIR}/body.md"
write_body "$body" T1 T1 none n/a n/a n/a n/a
expect_pass "ordinary T1" "$body" src/HappyGymStats.Core/Foo.cs
write_body "$body" T1 'T1,T4' none n/a n/a n/a n/a
expect_fail "extra observed tier rejected" "marked observed but is not required" "$body" src/HappyGymStats.Core/Foo.cs
write_body "$body" T1 T1 none n/a n/a n/a n/a
expect_fail "stronger diff cannot claim T1" "required evidence declaration disagrees" "$body" src/HappyGymStats.Data/Queries/PostgresQuery.cs
write_body "$body" T2 none T2 n/a "pending screenshot inspection" n/a n/a
expect_pass "visual gap explicit" "$body" src/HappyGymStats.Blazor/Pages/War.razor
write_body "$body" T1 T1 none n/a n/a n/a n/a
expect_fail "security change needs negative control" "security-negative-control must name" "$body" src/HappyGymStats.Api/Controllers/ImportController.cs
write_body "$body" T1 T1 none "cross-tenant import: caller B receives 409 and no caller A identity/token" n/a n/a n/a
expect_pass "security change with negative control" "$body" src/HappyGymStats.Api/Controllers/ImportController.cs
write_body "$body" T1 T1 none "fake security claim" n/a n/a n/a
expect_fail "ordinary diff cannot claim security negative" "must be n/a" "$body" src/HappyGymStats.Core/Foo.cs
if (( failures > 0 )); then echo "PR_EVIDENCE_REGRESSION_FAIL failures=$failures" >&2; exit 1; fi
echo PR_EVIDENCE_REGRESSION_PASS
