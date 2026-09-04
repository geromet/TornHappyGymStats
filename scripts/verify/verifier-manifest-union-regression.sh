#!/usr/bin/env bash
set -euo pipefail

# Regression proof for #57's completeness gap: build-and-test.sh's preflight
# must fail closed on a dependency it learned from the manifest, not only on
# the small hardcoded bash/dotnet set. verifier-dependency-regression.sh
# already proves a single verifier's own preflight fails closed (the rg
# example); this proves the UNION mechanism in build-and-test.sh itself does,
# by injecting a dependency that exists nowhere but a throwaway copy of the
# manifest.
readonly ROOT_DIR="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands bash sed mktemp rm grep

readonly fake_dependency="definitely-not-a-real-command-xyz"
readonly sabotaged_manifest="$(mktemp)"
cleanup() {
  rm -f "${sabotaged_manifest}"
}
trap cleanup EXIT

# Append the fake dependency to a real required row (privacy-verifier-regression)
# rather than inventing a new row, so this exercises the same parsing path as
# every other required row's dependencies column.
sed -E "s/^(privacy-verifier-regression\t[^\t]+\toffline\trequired\t)([^\t]+)/\1\2,${fake_dependency}/" \
  scripts/verify/manifest.tsv > "${sabotaged_manifest}"

grep -Fq "${fake_dependency}" "${sabotaged_manifest}" \
  || verify_die "sabotage setup failed: fake dependency not present in the mutated manifest"

status=0
output="$(HAPPYGYMSTATS_VERIFY_MANIFEST="${sabotaged_manifest}" bash scripts/verify/build-and-test.sh 2>&1)" || status=$?

(( status != 0 )) || verify_die "build-and-test.sh succeeded with a manifest-only dependency unavailable"
printf '%s\n' "${output}" | grep -Fq "ERROR: required command unavailable: ${fake_dependency}" \
  || verify_die "manifest-sourced dependency failure was not reported as unavailable. Output: ${output}"
if printf '%s\n' "${output}" | grep -Fq '==> dotnet build'; then
  verify_die "build-and-test.sh reached dotnet build despite an unavailable manifest-declared dependency. Output: ${output}"
fi

echo "PASS: a dependency known only from the manifest still fails the gate closed, before dotnet build ever runs"
