#!/usr/bin/env bash
set -euo pipefail

# Regression proof for #57: a verifier whose dependency is missing must fail as
# unavailable, never take the ordinary "no finding" path and print PASS.
readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands bash dirname env mktemp ln grep rm

restricted_bin="$(mktemp -d)"
cleanup() {
  rm -rf "${restricted_bin}"
}
trap cleanup EXIT

# Give the child just enough tooling to enter the representative verifier. In
# particular, do not provide rg: no-raw-playerid-log-templates.sh must diagnose
# that absence through verify_require_command before making any assertion.
ln -s "$(command -v bash)" "${restricted_bin}/bash"
ln -s "$(command -v dirname)" "${restricted_bin}/dirname"

status=0
output="$(env PATH="${restricted_bin}" bash scripts/verify/no-raw-playerid-log-templates.sh 2>&1)" || status=$?

(( status != 0 )) || verify_die "representative verifier succeeded with ripgrep unavailable"
printf '%s\n' "${output}" | grep -Fq 'ERROR: required command unavailable: rg' \
  || verify_die "missing-rg failure did not report the dependency as unavailable. Output: ${output}"
if printf '%s\n' "${output}" | grep -Fq 'PASS:'; then
  verify_die "verifier printed PASS after a dependency failure. Output: ${output}"
fi

echo "PASS: missing verifier dependency is explicit, non-zero, and cannot print PASS"
