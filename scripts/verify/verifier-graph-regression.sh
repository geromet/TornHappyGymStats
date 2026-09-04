#!/usr/bin/env bash
# verifier-graph-regression.sh — negative controls for verifier routing and fail-closed execution.
#
# A validator nobody has watched fail is a validator nobody knows works. This
# builds throwaway repository shapes and asserts failures happen FOR THE STATED
# REASON — not merely that a command exited non-zero, which any crash satisfies.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly GRAPH="${SCRIPT_DIR}/verifier-graph.sh"
readonly PRIVACY_VERIFIER="${SCRIPT_DIR}/no-raw-playerid-log-templates.sh"
readonly VERIFY_COMMON="${SCRIPT_DIR}/verify-common.sh"
# shellcheck source=scripts/verify/verify-common.sh
source "${VERIFY_COMMON}"

verify_require_commands bash mktemp mkdir rm chmod cat cp ln grep wc rg dirname
verify_require_file "${GRAPH}"
verify_require_file "${PRIVACY_VERIFIER}"

tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT
mkdir -p "${tmp}/scripts/verify"

printf '#!/usr/bin/env bash\nexit 0\n' > "${tmp}/scripts/verify/good.sh"
chmod +x "${tmp}/scripts/verify/good.sh"

run_graph() {
  HAPPYGYMSTATS_VERIFY_MANIFEST="$1" \
  HAPPYGYMSTATS_VERIFY_DIRECTORY="${tmp}/scripts/verify" \
  HAPPYGYMSTATS_VERIFY_REPO_ROOT="${tmp}" \
    bash "${GRAPH}" 2>&1
}

expect_graph_failure() {
  local label="$1" needle="$2" manifest="$3"
  local output status
  set +e
  output="$(run_graph "${manifest}")"
  status=$?
  set -e

  if (( status == 0 )); then
    printf '%s\n' "${output}"
    printf 'FAIL: %s unexpectedly passed\n' "${label}" >&2
    exit 1
  fi
  if [[ "${output}" != *"${needle}"* ]]; then
    printf '%s\n' "${output}"
    printf 'FAIL: %s failed for the wrong reason; expected %q\n' "${label}" "${needle}" >&2
    exit 1
  fi
  printf '  ok  rejects %s\n' "${label}"
}

echo "==> Negative controls for the verifier graph"

cat > "${tmp}/valid.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	offline	required	bash	-
EOF
run_graph "${tmp}/valid.tsv" >/dev/null || { printf 'FAIL: the valid baseline manifest was rejected\n' >&2; exit 1; }
echo "  ok  accepts a valid manifest"

touch "${tmp}/scripts/verify/stray.sh"
expect_graph_failure "an unregistered verifier file" "unregistered verifier file" "${tmp}/valid.tsv"
rm "${tmp}/scripts/verify/stray.sh"

cat > "${tmp}/stale.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
gone	scripts/verify/gone.sh	offline	required	bash	-
EOF
expect_graph_failure "a stale entry for a deleted file" "stale manifest entry" "${tmp}/stale.tsv"

cat > "${tmp}/duplicate-id.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	offline	required	bash	-
good	scripts/verify/good-2.sh	offline	required	bash	-
EOF
expect_graph_failure "a duplicate id" "duplicate id 'good'" "${tmp}/duplicate-id.tsv"

cat > "${tmp}/duplicate-path.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good-a	scripts/verify/good.sh	offline	required	bash	-
good-b	scripts/verify/good.sh	offline	required	bash	-
EOF
expect_graph_failure "a duplicate script path" "duplicate script 'scripts/verify/good.sh'" "${tmp}/duplicate-path.tsv"

cat > "${tmp}/missing-reason.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	operator	excluded	bash	-
EOF
expect_graph_failure "an exclusion with no reason" "needs a concrete exclusion reason" "${tmp}/missing-reason.tsv"

cat > "${tmp}/bad-gate.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	offline	maybe	bash	-
EOF
expect_graph_failure "an unknown gate value" "gate must be 'required' or 'excluded'" "${tmp}/bad-gate.tsv"

cat > "${tmp}/bad-header.tsv" <<'EOF'
id	script	tier	gate	dependencies
good	scripts/verify/good.sh	offline	required	bash
EOF
expect_graph_failure "a malformed header" "invalid header" "${tmp}/bad-header.tsv"

cat > "${tmp}/empty.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
EOF
expect_graph_failure "a manifest with no rows" "contains no verifier rows" "${tmp}/empty.tsv"

printf 'PASS: verifier graph rejects unregistered, stale, duplicate, malformed and unexplained entries\n'

# -----------------------------------------------------------------------------
# Dependency/sabotage controls for the privacy verifier.
#
# This is the real failure class that motivated #57: an inverted search used to
# print PASS when rg was missing or could not search. Keep the exact verifier in
# the loop, with stdin=/dev/null (the old false-green condition), rather than
# unit-testing a helper in isolation.
# -----------------------------------------------------------------------------
echo "==> Negative controls for fail-closed verifier execution"
readonly PRIVACY_ROOT="${tmp}/privacy-fixture"
readonly PRIVACY_SCRIPT="${PRIVACY_ROOT}/scripts/verify/no-raw-playerid-log-templates.sh"
readonly SAFE_BIN="${PRIVACY_ROOT}/safe-bin"
mkdir -p "${PRIVACY_ROOT}/scripts/verify" "${PRIVACY_ROOT}/src" "${SAFE_BIN}"
cp "${PRIVACY_VERIFIER}" "${PRIVACY_SCRIPT}"
cp "${VERIFY_COMMON}" "${PRIVACY_ROOT}/scripts/verify/verify-common.sh"
printf '%s\n' 'namespace Fixture; internal static class Good { }' > "${PRIVACY_ROOT}/src/Good.cs"

# Minimal PATH for the child: enough for the verifier to start, intentionally no
# rg. Use absolute symlink targets resolved before PATH is restricted.
for command_name in dirname grep wc; do
  command_path="$(command -v "${command_name}")"
  ln -s "${command_path}" "${SAFE_BIN}/${command_name}"
done
readonly BASH_BIN="$(command -v bash)"

run_privacy() {
  local child_path="$1"
  set +e
  PRIVACY_OUTPUT="$(PATH="${child_path}" "${BASH_BIN}" "${PRIVACY_SCRIPT}" </dev/null 2>&1)"
  PRIVACY_STATUS=$?
  set -e
}

expect_privacy_failure() {
  local label="$1" expected_status="$2" needle="$3" child_path="$4"
  run_privacy "${child_path}"
  if (( PRIVACY_STATUS != expected_status )); then
    printf '%s\n' "${PRIVACY_OUTPUT}" >&2
    printf 'FAIL: %s exited %d; expected %d\n' "${label}" "${PRIVACY_STATUS}" "${expected_status}" >&2
    exit 1
  fi
  if [[ "${PRIVACY_OUTPUT}" != *"${needle}"* ]]; then
    printf '%s\n' "${PRIVACY_OUTPUT}" >&2
    printf 'FAIL: %s failed for the wrong reason; expected %q\n' "${label}" "${needle}" >&2
    exit 1
  fi
  if [[ "${PRIVACY_OUTPUT}" == *"PASS:"* ]]; then
    printf '%s\n' "${PRIVACY_OUTPUT}" >&2
    printf 'FAIL: %s emitted PASS while its proof was unavailable/violated\n' "${label}" >&2
    exit 1
  fi
  printf '  ok  %s fails closed (exit %d)\n' "${label}" "${expected_status}"
}

# 1. Tool absent: preflight must fail as infrastructure (2), never clean (0).
expect_privacy_failure \
  "missing rg" 2 "required command unavailable: rg" "${SAFE_BIN}"

# 2. Tool resolves but is broken: listing source files must preserve the rg error.
cat > "${SAFE_BIN}/rg" <<'EOF'
#!/bin/sh
exit 127
EOF
chmod +x "${SAFE_BIN}/rg"
expect_privacy_failure \
  "broken rg" 2 "ripgrep failed (exit 127)" "${SAFE_BIN}"

# 3. Tool lies that there are zero source files: a zero-file search proves nothing.
cat > "${SAFE_BIN}/rg" <<'EOF'
#!/bin/sh
exit 0
EOF
chmod +x "${SAFE_BIN}/rg"
expect_privacy_failure \
  "zero-file search" 2 "no files matching '*.cs'" "${SAFE_BIN}"

# 4. Real clean baseline still passes with stdin=/dev/null.
run_privacy "${PATH}"
if (( PRIVACY_STATUS != 0 )) || [[ "${PRIVACY_OUTPUT}" != *"PASS: no raw player-id log templates found"* ]]; then
  printf '%s\n' "${PRIVACY_OUTPUT}" >&2
  printf 'FAIL: clean privacy fixture did not pass\n' >&2
  exit 1
fi
echo "  ok  clean privacy fixture passes under non-interactive stdin"

# 5. Plant the exact forbidden mechanism and require a real finding (1), not an
# infrastructure error (2). This is the checked-in sabotage test the old script
# never had.
printf '%s\n' 'logger.LogInformation("Torn player {TornPlayerId}", id);' > "${PRIVACY_ROOT}/src/Violation.cs"
expect_privacy_failure \
  "planted raw player-id log template" 1 "raw player-id log template detected" "${PATH}"

echo "PASS: verifier dependency/privacy regressions distinguish clean, finding, and unavailable proof"
