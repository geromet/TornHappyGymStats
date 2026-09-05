#!/usr/bin/env bash
# shellcheck shell=bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
base="${HAPPYGYMSTATS_REMOTE_POLICY_BASE_SHA:-}"
if [[ -n "$base" ]] && git cat-file -e "$base^{commit}" 2>/dev/null; then :
elif git rev-parse --verify HEAD^1 >/dev/null 2>&1; then base="$(git rev-parse HEAD^1)"
elif git rev-parse --verify origin/main >/dev/null 2>&1; then base="$(git merge-base origin/main HEAD)"
else echo 'REMOTE_TRANSPORT_POLICY_FAIL: no comparison base; refusing false green.' >&2; exit 1
fi
scan() {
  local path='' line added
  while IFS= read -r line; do
    [[ "$line" == '+++ b/'* ]] && { path="${line#+++ b/}"; continue; }
    [[ "$line" == +* && "$line" != +++* ]] || continue
    added="${line#+}"
    case "$path" in
      scripts/lib/remote-exec.sh|scripts/verify/remote-transport-policy.sh|scripts/verify/remote-exec-pty-integration.sh) continue ;;
    esac
    if [[ "$added" =~ (^|[[:space:];\&\|\(\)])(ssh|scp)([[:space:]]|$) ]] && [[ "$added" != *'HGS_RAW_SSH_EXEMPT:'* ]]; then
      printf '%s: %s\n' "${path:-<unknown>}" "$added"
    fi
  done
}
violations="$(git diff --unified=0 --no-ext-diff "$base" HEAD -- 'scripts/*.sh' 'scripts/**/*.sh' | scan)"
if [[ -n "$violations" ]]; then
  echo 'REMOTE_TRANSPORT_POLICY_FAIL: new raw operational ssh/scp must use scripts/lib/remote-exec.sh or an explicit HGS_RAW_SSH_EXEMPT reason.' >&2
  printf '%s\n' "$violations" >&2
  exit 1
fi
# Sabotage proof for the scanner itself.
[[ -n "$(printf '%s\n' '+++ b/scripts/deploy-example.sh' '+ssh example.invalid true' | scan)" ]] || { echo 'REMOTE_TRANSPORT_POLICY_FAIL: negative control escaped.' >&2; exit 1; }
[[ -z "$(printf '%s\n' '+++ b/scripts/deploy-example.sh' '+ssh example.invalid true # HGS_RAW_SSH_EXEMPT: fixture boundary' | scan)" ]] || { echo 'REMOTE_TRANSPORT_POLICY_FAIL: explicit exemption rejected.' >&2; exit 1; }
echo "REMOTE_TRANSPORT_POLICY_PASS base=$base"
