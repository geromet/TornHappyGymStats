#!/usr/bin/env bash
# remote-heredoc-lint.sh — Catch local expansion leaking into remote scripts.
#
# SCRIPT_CATEGORY=verify
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Offline static check. No SSH, no host.
#
# The deploy scripts send remote payloads through UNQUOTED heredocs, because
# they need local variables interpolated:
#
#     remote_exec_script <<REMOTE
#     DO_SWAP=${DO_SWAP}          <-- deliberate: expanded locally
#     echo "\${SUDO} ..."         <-- must reach the remote as-is
#     REMOTE
#
# Everything meant to run on the SERVER must therefore be escaped. Two bugs of
# this exact shape reached the operator:
#
#   * a comment containing backticks ran on the LOCAL machine, printing a sudo
#     usage block and then a syntax error
#   * an unescaped $1 inside a nested heredoc aborted the run with
#     "$1: unbound variable" under set -u
#
# A nested quoted heredoc does NOT protect its contents: the outer unquoted
# heredoc is parsed first, so the inner text is expanded before it is ever
# written. That is what broke the docker-firewall step.
set -uo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${ROOT_DIR}" || exit 1

failures=0
fail() { echo "[FAIL] $1" >&2; failures=$((failures + 1)); }
pass() { echo "[PASS] $1"; }

readonly TARGETS=(
  scripts/setup-auto-reboot.sh
  scripts/post-reboot-maintenance.sh
  scripts/upgrade-containers.sh
  scripts/remove-teamspeak.sh
  scripts/check-server-env.sh
)

echo "==> remote-heredoc lint"

for f in "${TARGETS[@]}"; do
  [[ -f "${f}" ]] || { fail "missing: ${f}"; continue; }

  # 1. Backticks anywhere in an unquoted remote heredoc are command
  #    substitution on the LOCAL machine.
  backticks="$(awk '
    /remote_exec_script.*<<REMOTE([^'"'"']|$)/ { inside=1; next }
    inside && /^REMOTE$/ { inside=0; next }
    inside && /`/ { printf "  %s:%d: %s\n", FILENAME, NR, $0 }
  ' "${f}")"
  if [[ -n "${backticks}" ]]; then
    fail "${f}: backticks inside an unquoted heredoc (run locally, not remotely)"
    printf '%s\n' "${backticks}" >&2
  fi

  # 2. Positional parameters. A remote function's "$1" expands to the LOCAL
  #    script's $1 — usually unset, which is fatal under set -u.
  positional="$(awk '
    /remote_exec_script.*<<REMOTE([^'"'"']|$)/ { inside=1; next }
    inside && /^REMOTE$/ { inside=0; next }
    inside && /(^|[^\\])\$[0-9@*]/ { printf "  %s:%d: %s\n", FILENAME, NR, $0 }
  ' "${f}")"
  if [[ -n "${positional}" ]]; then
    fail "${f}: unescaped positional parameter inside an unquoted heredoc"
    printf '%s\n' "${positional}" >&2
  fi

  # 3. Arithmetic and command substitution meant for the remote side.
  subst="$(awk '
    /remote_exec_script.*<<REMOTE([^'"'"']|$)/ { inside=1; next }
    inside && /^REMOTE$/ { inside=0; next }
    inside && /(^|[^\\])\$\(/ { printf "  %s:%d: %s\n", FILENAME, NR, $0 }
  ' "${f}")"
  if [[ -n "${subst}" ]]; then
    fail "${f}: unescaped \$( ) inside an unquoted heredoc — runs locally"
    printf '%s\n' "${subst}" >&2
  fi

  [[ -z "${backticks}${positional}${subst}" ]] && pass "${f}: no local-expansion leaks"
done

# 4. The interactive ssh must never be piped or captured, and must be handed the
#    controlling terminal, or sudo's prompt breaks in one of several ways.
LIB=scripts/lib/remote-exec.sh
if [[ -f "${LIB}" ]]; then
  if grep -qE '^\s*ssh -tt .*\| *(tee|sed)' "${LIB}"; then
    fail "${LIB}: the interactive ssh is piped — that breaks the tty and echoes the password"
  else
    pass "${LIB}: interactive ssh is unpiped"
  fi
  if grep -qE '^\s*ssh -tt .*< */dev/tty' "${LIB}"; then
    pass "${LIB}: ssh is given the controlling terminal"
  else
    fail "${LIB}: ssh stdin is not /dev/tty — the sudo password will be echoed in clear"
  fi
else
  fail "missing: ${LIB}"
fi

# 5. Every script parses.
for f in "${TARGETS[@]}" "${LIB}"; do
  [[ -f "${f}" ]] || continue
  bash -n "${f}" 2>/dev/null && pass "bash -n clean: ${f}" || fail "bash -n failed: ${f}"
done

echo
if (( failures > 0 )); then
  echo "REMOTE_HEREDOC_LINT_FAIL failures=${failures}" >&2
  exit 1
fi
echo "REMOTE_HEREDOC_LINT_PASS failures=0"
