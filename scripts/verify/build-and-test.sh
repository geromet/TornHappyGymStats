#!/usr/bin/env bash
# build-and-test.sh — Build solution and run full test suite.
set -euo pipefail

usage() {
  cat <<EOF
Usage: bash scripts/verify/build-and-test.sh

Runs:
  1) dotnet build
  2) dotnet test
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

# PREFLIGHT BEFORE ANY VERIFIER CAN PRINT PASS.
#
# The gate used to carry a hand-maintained command list. That proved the current
# tools existed, but the list could drift when a required verifier gained a new
# dependency. The verifier manifest already declares those dependencies, so use
# it as the source of truth and preflight the union before any assertion runs.
ROOT_DIR="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

readonly VERIFY_MANIFEST="scripts/verify/manifest.tsv"
verify_require_file "${VERIFY_MANIFEST}"

declare -A gate_commands=(
  [bash]=1
  [dotnet]=1
  # Bootstrap/verifier-graph dependencies retained explicitly because the graph
  # runs before manifest routing can validate dependency metadata.
  [rg]=1
  [grep]=1
  [sed]=1
  [awk]=1
  [wc]=1
)

manifest_row=0
while IFS=$'\t' read -r _id _script _tier gate dependencies _exclusion_reason; do
  ((manifest_row += 1))
  (( manifest_row == 1 )) && continue
  [[ "${gate}" == "required" ]] || continue
  [[ -n "${dependencies}" && "${dependencies}" != "-" ]] || continue

  IFS=',' read -r -a dependency_list <<< "${dependencies}"
  for dependency in "${dependency_list[@]}"; do
    [[ -n "${dependency}" && "${dependency}" != "-" ]] || continue
    gate_commands["${dependency}"]=1
  done
done < "${VERIFY_MANIFEST}"

echo "==> preflight: tools the required gate depends on"
for command_name in "${!gate_commands[@]}"; do
  verify_require_command "${command_name}"
done
echo "PASS: all declared required-gate dependencies are available"

# ROUTING COMES FROM THE MANIFEST, NOT FROM THIS FILE.
#
# The verifier list used to live here by hand, so adding a verifier and adding it
# to the gate were separate acts and the second was easy to skip — the gate could
# lose coverage without any diff saying so. scripts/verify/manifest.tsv is now the
# only routing table: verifier-graph.sh fails if a script exists without a row, if
# a row points at a deleted script, or if an exclusion has no stated reason.
#
# Do not add a verifier call to this file. Create the script and add one row.
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

# A routing table that routes nothing would run the build and tests and report a
# green gate having proved none of the contracts.
(( required_run > 0 )) || verify_die "manifest routed no required verifiers"
echo "PASS: ${required_run} required verifiers ran"

echo "==> dotnet build"
dotnet build

# STILL `dotnet test`, DELIBERATELY.
#
# The #59 packet has this line swapped for hermetic-tests.sh. That is right once
# #60 has split the Postgres tier into its own non-skippable job — but #60 is not
# done, and hermetic-tests.sh excludes Category=PostgresApiIntegration. Swapping
# now would drop the Postgres tier from the canonical gate (367 tests to 364) with
# nothing saying so, which is the exact failure this issue exists to prevent.
#
# CI runs hermetic-tests.sh as its own step, so the hermetic guarantee is checked
# either way. Make the swap in #60, together with the job that picks Postgres up.
echo "==> dotnet test"
dotnet test
