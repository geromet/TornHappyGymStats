#!/usr/bin/env bash
# build-and-test.sh — Build solution and run the canonical hermetic test suite.
set -euo pipefail

usage() {
  cat <<EOF
Usage: bash scripts/verify/build-and-test.sh

Runs:
  1) required repository verifiers from scripts/verify/manifest.tsv
  2) dotnet build
  3) hermetic non-Postgres tests

The real Postgres tier is intentionally separate and non-skippable in CI via
scripts/verify/s07-postgres-integration.sh.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

# PREFLIGHT BEFORE ANY VERIFIER CAN PRINT PASS.
#
# The gate used to carry a hand-maintained command list. That proved the current
# tools existed, but the list could drift when a required verifier (or the
# hermetic test runner this file always invokes) gained a new dependency without
# the list here being updated to match. The manifest already declares those
# dependencies, so use it as the source of truth and preflight the union before
# any assertion runs. Only `bash`/`dotnet` are hardcoded, because this file
# needs `dotnet` directly for the build step and nothing here can preflight
# itself running under something other than bash.
ROOT_DIR="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

# Overridable so verifier-manifest-union-regression.sh can prove the union
# below actually comes from the manifest, not a value only this file knows.
readonly VERIFY_MANIFEST="${HAPPYGYMSTATS_VERIFY_MANIFEST:-scripts/verify/manifest.tsv}"
verify_require_file "${VERIFY_MANIFEST}"

declare -A gate_commands=(
  [bash]=1
  [dotnet]=1
)

# Union required-gate verifier dependencies, plus the hermetic test runner's own
# — it is invoked unconditionally below (step 3) even though its manifest tier
# is "test-runner", not "required", so a dependency only it needs would
# otherwise preflight-pass and then fail mid-run.
manifest_row=0
while IFS=$'\t' read -r id _script _tier gate dependencies _exclusion_reason; do
  ((manifest_row += 1))
  (( manifest_row == 1 )) && continue
  [[ "${gate}" == "required" || "${id}" == "hermetic-test-runner" ]] || continue
  [[ -n "${dependencies}" && "${dependencies}" != "-" ]] || continue

  IFS=',' read -r -a dependency_list <<< "${dependencies}"
  for dependency in "${dependency_list[@]}"; do
    [[ -n "${dependency}" && "${dependency}" != "-" ]] || continue
    gate_commands["${dependency}"]=1
  done
done < "${VERIFY_MANIFEST}"

echo "==> preflight: tools the required gate (and the hermetic test runner it always invokes) depend on"
for command_name in "${!gate_commands[@]}"; do
  verify_require_command "${command_name}"
done
echo "PASS: all declared required-gate dependencies are available"

# ROUTING COMES FROM THE MANIFEST, NOT FROM THIS FILE.
#
# Do not add a verifier call to this file. Create the script and add one row to
# scripts/verify/manifest.tsv; verifier-graph.sh rejects unregistered/stale rows.
echo "==> verify: verifier graph"
bash scripts/verify/verifier-graph.sh

manifest_row=0
required_run=0
while IFS=$'\t' read -r id script tier gate _dependencies _exclusion_reason; do
  ((manifest_row += 1))
  (( manifest_row == 1 )) && continue
  [[ "${gate}" == "required" ]] || continue

  echo "==> verify [${tier}]: ${id} (${script})"
  bash "${script}"
  ((required_run += 1))
done < "${VERIFY_MANIFEST}"

# A routing table that routes nothing would run the build/tests and report green
# having proved none of the repository contracts.
(( required_run > 0 )) || verify_die "manifest routed no required verifiers"
echo "PASS: ${required_run} required verifiers ran"

echo "==> dotnet build"
dotnet build

# #60 gives Postgres its own required CI job, so this entrypoint can finally
# answer one question consistently on a laptop and on a runner: does the ordinary
# suite pass without developer configuration or opportunistic database access?
echo "==> hermetic non-Postgres tests"
bash scripts/verify/hermetic-tests.sh --no-build
