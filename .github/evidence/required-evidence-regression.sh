#!/usr/bin/env bash
# Contract/negative tests for required-evidence.sh. No repository mutation.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly CLASSIFIER="${ROOT_DIR}/.github/evidence/required-evidence.sh"

failures=0
assert_eq() {
  local name="$1" expected="$2" actual="$3"
  if [[ "${actual}" != "${expected}" ]]; then
    printf 'FAIL: %s\n  expected: %q\n  actual:   %q\n' "${name}" "${expected}" "${actual}" >&2
    ((failures += 1))
  else
    printf 'PASS: %s\n' "${name}"
  fi
}

classify() {
  bash "${CLASSIFIER}" --files "$@"
}

assert_eq "unmatched/source defaults to T1" \
  "T1" \
  "$(classify src/HappyGymStats.Core/Models/Foo.cs)"

assert_eq "docs default to T1" \
  "T1" \
  "$(classify docs/example.md)"

assert_eq "Razor requires T2" \
  "T2" \
  "$(classify src/HappyGymStats.Blazor/Pages/War.razor)"

assert_eq "remote deployment requires T3" \
  "T3" \
  "$(classify scripts/deploy-backend.sh)"

assert_eq "Data/migration requires T4" \
  "T4" \
  "$(classify src/HappyGymStats.Data/Migrations/20260904_Test.cs)"

assert_eq "mixed Razor + migration + remote returns the union" \
  $'T2\nT3\nT4' \
  "$(classify \
      src/HappyGymStats.Blazor/Pages/War.razor \
      src/HappyGymStats.Data/Migrations/20260904_Test.cs \
      scripts/remote-exec.sh)"

assert_eq "one path may require more than one stronger tier" \
  $'T2\nT4' \
  "$(classify src/HappyGymStats.Data/Views/RelationalStatus.razor)"

assert_eq "machine-readable JSON is stable" \
  '{"tiers":["T2","T4"]}' \
  "$(bash "${CLASSIFIER}" --format json --files \
      src/HappyGymStats.Blazor/Pages/War.razor \
      src/HappyGymStats.Data/Queries/PostgresQuery.cs)"

# Exceptions/rules without a checked-in reason must be rejected. Otherwise a
# future agent can weaken evidence by adding a mysterious path carve-out.
tmp_rules="$(mktemp)"
trap 'rm -f "${tmp_rules}"' EXIT
printf 'pattern\ttier\treason\n*.razor\tT2\t\n' > "${tmp_rules}"
if EVIDENCE_RULES_FILE="${tmp_rules}" bash "${CLASSIFIER}" --files Foo.razor >/tmp/hgs-evidence-out.$$ 2>/tmp/hgs-evidence-err.$$; then
  echo "FAIL: rule without a reason was accepted" >&2
  ((failures += 1))
else
  if grep -q 'missing checked-in reason' /tmp/hgs-evidence-err.$$; then
    echo "PASS: reasonless rule is rejected for the intended reason"
  else
    echo "FAIL: reasonless rule failed for the wrong reason" >&2
    cat /tmp/hgs-evidence-err.$$ >&2
    ((failures += 1))
  fi
fi
rm -f /tmp/hgs-evidence-out.$$ /tmp/hgs-evidence-err.$$

if (( failures > 0 )); then
  printf 'REQUIRED_EVIDENCE_REGRESSION_FAIL failures=%d\n' "${failures}" >&2
  exit 1
fi

echo "REQUIRED_EVIDENCE_REGRESSION_PASS"
