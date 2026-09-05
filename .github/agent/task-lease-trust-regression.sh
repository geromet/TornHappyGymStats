#!/usr/bin/env bash
# Deterministic trust-boundary controls for .github/workflows/task-lease.yml.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly WORKFLOW="${ROOT_DIR}/.github/workflows/task-lease.yml"
readonly TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

for command_name in awk grep mktemp; do
  command -v "$command_name" >/dev/null 2>&1 || exit 2
done

job_block() {
  local job="$1"
  awk -v wanted="  ${job}:" '
    $0 == wanted { in_job=1; print; next }
    in_job && /^  [A-Za-z0-9_-]+:/ { exit }
    in_job { print }
  ' "$WORKFLOW"
}

readonly FANOUT_BLOCK="$(job_block fanout)"
readonly LEASE_BLOCK="$(job_block lease)"
failures=0

fail() {
  echo "FAIL: $*" >&2
  ((failures+=1))
}

if grep -q '^permissions:' "$WORKFLOW"; then
  fail "workflow-level permissions would leak write authority into PR-code jobs"
else
  echo "PASS: no workflow-level token permission grant"
fi

if grep -q '^      actions: write$' <<<"$FANOUT_BLOCK"; then
  echo "PASS: trusted fanout alone receives actions:write"
else
  fail "fanout must explicitly own the actions:write permission used for workflow dispatch"
fi

if grep -Eq 'actions/checkout|\.github/agent/' <<<"$FANOUT_BLOCK"; then
  fail "actions:write fanout must not checkout or execute repository/PR-controlled code"
else
  echo "PASS: privileged fanout does not execute repository code"
fi

for required in 'contents: read' 'issues: read' 'pull-requests: read'; do
  if grep -q "^      ${required}$" <<<"$LEASE_BLOCK"; then
    echo "PASS: lease declares ${required}"
  else
    fail "lease is missing explicit ${required} permission"
  fi
done

lease_actions_permission="$(awk '
  /^    permissions:/ { in_permissions=1; next }
  in_permissions && /^    [^ ]/ { exit }
  in_permissions && $1 == "actions:" { print $2; exit }
' <<<"$LEASE_BLOCK")"

if [[ "$lease_actions_permission" == "write" ]]; then
  fail "lease executes PR-head code and therefore must not receive actions:write"
else
  echo "PASS: PR-head lease job has no actions:write authority"
fi

if ! grep -q 'ref: \${{ env.PR_HEAD_SHA }}' <<<"$LEASE_BLOCK"; then
  fail "negative control expects lease to identify the actual PR-head execution boundary"
fi

# Synthetic malicious PR-head code: it attempts a privileged effect only when the
# permission extracted from the real lease job says Actions write is available.
# This keeps the regression secret-free and non-destructive while proving the
# workflow contract that untrusted branch code cannot inherit dispatch authority.
cat > "$TMP/untrusted-pr-head.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${SIMULATED_ACTIONS_PERMISSION:-none}" == "write" ]]; then
  : > "${PRIVILEGED_EFFECT_MARKER:?}"
fi
EOF
chmod +x "$TMP/untrusted-pr-head.sh"
readonly MARKER="$TMP/SHOULD_NOT_EXECUTE_PRIVILEGED_EFFECT"
SIMULATED_ACTIONS_PERMISSION="${lease_actions_permission:-none}" \
  PRIVILEGED_EFFECT_MARKER="$MARKER" \
  bash "$TMP/untrusted-pr-head.sh"

if [[ -e "$MARKER" ]]; then
  fail "synthetic PR-head code observed write authority and performed privileged marker effect"
else
  echo "PASS: synthetic malicious PR-head code cannot perform write-scoped Actions effect"
fi

if ((failures > 0)); then
  echo "TASK_LEASE_TRUST_REGRESSION_FAIL failures=$failures" >&2
  exit 1
fi

echo TASK_LEASE_TRUST_REGRESSION_PASS
