#!/usr/bin/env bash
# Contract/negative tests for required-evidence.sh. No repository mutation.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly CLASSIFIER="${ROOT_DIR}/.github/evidence/required-evidence.sh"

failures=0
assert_eq() {
  local name="$1" expected="$2" actual="$3"
  if [[ "${actual}" != "${expected}" ]]; then
    printf 'FAIL: %s\n  expected: %q\n  actual:   %q\n' "$name" "$expected" "$actual" >&2
    ((failures += 1))
  else
    printf 'PASS: %s\n' "$name"
  fi
}

classify() { bash "$CLASSIFIER" --files "$@"; }
security() { bash "$CLASSIFIER" --security-boundary --files "$@"; }

assert_eq "unmatched/source defaults to T1" "T1" "$(classify src/HappyGymStats.Core/Models/Foo.cs)"
assert_eq "docs default to T1" "T1" "$(classify docs/example.md)"
assert_eq "Razor requires T2" "T2" "$(classify src/HappyGymStats.Blazor/Pages/War.razor)"
assert_eq "remote deployment requires T3" "T3" "$(classify scripts/deploy-backend.sh)"
assert_eq "operator menu requires T3" "T3" "$(classify scripts/menu.sh)"
assert_eq "operator registry requires T3" "T3" "$(classify scripts/lib/registry.sh)"
assert_eq "Data/migration requires T4" "T4" "$(classify src/HappyGymStats.Data/Migrations/20260904_Test.cs)"
assert_eq "mixed Razor + migration + remote returns the union" $'T2\nT3\nT4' "$(classify src/HappyGymStats.Blazor/Pages/War.razor src/HappyGymStats.Data/Migrations/20260904_Test.cs scripts/remote-exec.sh)"
assert_eq "one path may require more than one stronger tier" $'T2\nT4' "$(classify src/HappyGymStats.Data/Views/RelationalStatus.razor)"
assert_eq "ordinary source leaves security boundary unchanged" 'security_boundary=unchanged' "$(security src/HappyGymStats.Core/Models/Foo.cs)"
assert_eq "import ownership marks security boundary changed" 'security_boundary=changed' "$(security src/HappyGymStats.Api/Controllers/ImportController.cs)"
assert_eq "nginx/infra marks security boundary changed" 'security_boundary=changed' "$(security infra/nginx-torn.conf)"
assert_eq "machine-readable JSON includes both policy dimensions" '{"tiers":["T2","T4"],"security_boundary":"unchanged"}' "$(bash "$CLASSIFIER" --format json --files src/HappyGymStats.Blazor/Pages/War.razor src/HappyGymStats.Data/Queries/PostgresQuery.cs)"

tmp_rules="$(mktemp)"
trap 'rm -f "${tmp_rules}"' EXIT
printf 'pattern\ttier\tsecurity\treason\n*.razor\tT2\tunchanged\t\n' > "$tmp_rules"
if EVIDENCE_RULES_FILE="$tmp_rules" bash "$CLASSIFIER" --files Foo.razor >/tmp/hgs-evidence-out.$$ 2>/tmp/hgs-evidence-err.$$; then
  echo "FAIL: rule without a reason was accepted" >&2; ((failures += 1))
elif grep -q 'missing checked-in reason' /tmp/hgs-evidence-err.$$; then
  echo "PASS: reasonless rule is rejected for the intended reason"
else
  echo "FAIL: reasonless rule failed for the wrong reason" >&2; cat /tmp/hgs-evidence-err.$$ >&2; ((failures += 1))
fi
printf 'pattern\ttier\tsecurity\treason\n*.razor\tT2\tmaybe\ttest\n' > "$tmp_rules"
if EVIDENCE_RULES_FILE="$tmp_rules" bash "$CLASSIFIER" --files Foo.razor >/tmp/hgs-evidence-out.$$ 2>/tmp/hgs-evidence-err.$$; then
  echo "FAIL: invalid security flag was accepted" >&2; ((failures += 1))
elif grep -q 'invalid security flag' /tmp/hgs-evidence-err.$$; then
  echo "PASS: invalid security flag is rejected"
else
  echo "FAIL: invalid security flag failed for the wrong reason" >&2; cat /tmp/hgs-evidence-err.$$ >&2; ((failures += 1))
fi
rm -f /tmp/hgs-evidence-out.$$ /tmp/hgs-evidence-err.$$

if (( failures > 0 )); then printf 'REQUIRED_EVIDENCE_REGRESSION_FAIL failures=%d\n' "$failures" >&2; exit 1; fi
echo "REQUIRED_EVIDENCE_REGRESSION_PASS"
