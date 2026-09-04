#!/usr/bin/env bash
# verifier-graph-regression.sh — the negative control for verifier-graph.sh.
#
# A validator nobody has watched fail is a validator nobody knows works. This
# builds a throwaway repository shape, feeds verifier-graph.sh manifests that are
# each broken in exactly one way, and asserts it rejects them FOR THE STATED
# REASON — not merely that it exited non-zero, which any crash would satisfy.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly GRAPH="${SCRIPT_DIR}/verifier-graph.sh"

[[ -f "${GRAPH}" ]] || { printf 'FAIL: %s missing\n' "${GRAPH}" >&2; exit 2; }

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

expect_failure() {
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
expect_failure "an unregistered verifier file" "unregistered verifier file" "${tmp}/valid.tsv"
rm "${tmp}/scripts/verify/stray.sh"

cat > "${tmp}/stale.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
gone	scripts/verify/gone.sh	offline	required	bash	-
EOF
expect_failure "a stale entry for a deleted file" "stale manifest entry" "${tmp}/stale.tsv"

cat > "${tmp}/duplicate-id.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	offline	required	bash	-
good	scripts/verify/good-2.sh	offline	required	bash	-
EOF
expect_failure "a duplicate id" "duplicate id 'good'" "${tmp}/duplicate-id.tsv"

cat > "${tmp}/duplicate-path.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good-a	scripts/verify/good.sh	offline	required	bash	-
good-b	scripts/verify/good.sh	offline	required	bash	-
EOF
expect_failure "a duplicate script path" "duplicate script 'scripts/verify/good.sh'" "${tmp}/duplicate-path.tsv"

cat > "${tmp}/missing-reason.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	operator	excluded	bash	-
EOF
expect_failure "an exclusion with no reason" "needs a concrete exclusion reason" "${tmp}/missing-reason.tsv"

cat > "${tmp}/bad-gate.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
good	scripts/verify/good.sh	offline	maybe	bash	-
EOF
expect_failure "an unknown gate value" "gate must be 'required' or 'excluded'" "${tmp}/bad-gate.tsv"

cat > "${tmp}/bad-header.tsv" <<'EOF'
id	script	tier	gate	dependencies
good	scripts/verify/good.sh	offline	required	bash
EOF
expect_failure "a malformed header" "invalid header" "${tmp}/bad-header.tsv"

cat > "${tmp}/empty.tsv" <<'EOF'
id	script	tier	gate	dependencies	exclusion_reason
EOF
expect_failure "a manifest with no rows" "contains no verifier rows" "${tmp}/empty.tsv"

printf 'PASS: verifier graph rejects unregistered, stale, duplicate, malformed and unexplained entries\n'
