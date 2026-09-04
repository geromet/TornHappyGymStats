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
# These are real transitive dependencies of the canonical gate, including the
# clean-environment test runner. A missing tool is an unavailable proof, never a
# successful "no violations found" result.
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
echo "==> preflight: tools the gate depends on"
verify_require_commands rg dotnet grep sed awk wc env find sort head cut
echo "PASS: canonical gate tool dependencies are present"

# ROUTING COMES FROM THE MANIFEST, NOT FROM THIS FILE.
#
# Do not add a verifier call to this file. Create the script and add one row to
# scripts/verify/manifest.tsv; verifier-graph.sh rejects unregistered/stale rows.
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
