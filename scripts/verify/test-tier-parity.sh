#!/usr/bin/env bash
# Prove the explicit UnitTests + IntegrationTests projects cover the legacy suite
# exactly once before the physical source-file migration removes the legacy project.
set -euo pipefail

ROOT_DIR="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd)"
cd "${ROOT_DIR}"

legacy="tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
unit="tests/HappyGymStats.UnitTests/HappyGymStats.UnitTests.csproj"
integration="tests/HappyGymStats.IntegrationTests/HappyGymStats.IntegrationTests.csproj"

for project in "${legacy}" "${unit}" "${integration}"; do
  [[ -f "${project}" ]] || { echo "FAIL: missing ${project}" >&2; exit 1; }
done
command -v dotnet >/dev/null 2>&1 || { echo "FAIL: dotnet is required" >&2; exit 1; }

tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT

list_tests() {
  local project="$1"
  local output="$2"
  dotnet test "${project}" --nologo --list-tests --no-restore \
    | awk '/The following Tests are available:/{capture=1; next} capture && NF { sub(/^[[:space:]]+/, ""); print }' \
    | sort -u > "${output}"
}

list_tests "${legacy}" "${tmp}/legacy"
list_tests "${unit}" "${tmp}/unit"
list_tests "${integration}" "${tmp}/integration"

cat "${tmp}/unit" "${tmp}/integration" | sort > "${tmp}/tiered-all"
sort -u "${tmp}/tiered-all" > "${tmp}/tiered-union"

duplicates="$(comm -12 "${tmp}/unit" "${tmp}/integration" || true)"
if [[ -n "${duplicates}" ]]; then
  echo "FAIL: tests are assigned to both UnitTests and IntegrationTests:" >&2
  printf '%s\n' "${duplicates}" >&2
  exit 1
fi

if ! diff -u "${tmp}/legacy" "${tmp}/tiered-union"; then
  echo "FAIL: explicit tiers do not preserve the legacy test inventory" >&2
  exit 1
fi

legacy_count="$(wc -l < "${tmp}/legacy")"
unit_count="$(wc -l < "${tmp}/unit")"
integration_count="$(wc -l < "${tmp}/integration")"

(( legacy_count > 0 )) || { echo "FAIL: legacy discovery returned zero tests" >&2; exit 1; }
(( unit_count > 0 )) || { echo "FAIL: unit tier discovered zero tests" >&2; exit 1; }
(( integration_count > 0 )) || { echo "FAIL: integration tier discovered zero tests" >&2; exit 1; }
(( unit_count + integration_count == legacy_count )) || {
  echo "FAIL: count mismatch legacy=${legacy_count} unit=${unit_count} integration=${integration_count}" >&2
  exit 1
}

printf 'TEST TIER PARITY — legacy=%d unit=%d integration=%d total=%d\n' \
  "${legacy_count}" "${unit_count}" "${integration_count}" "$((unit_count + integration_count))"
echo "PASS: every legacy test is assigned to exactly one explicit runtime tier"
