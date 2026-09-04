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
# The gate transitively needs these. ripgrep is the one that actually bites: it
# is absent from the GitHub runner, and its absence is what let the raw-player-id
# check report PASS in CI without opening a file. The rest are present on both a
# workstation and the runner, so preflighting them is cheap insurance rather than
# a live fix — but a missing tool must be its own loud failure, never a pass.
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
echo "==> preflight: tools the gate depends on"
verify_require_commands rg dotnet grep sed awk wc
echo "PASS: rg, dotnet, grep, sed, awk, wc all present"

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

readonly VERIFY_MANIFEST="scripts/verify/manifest.tsv"
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
