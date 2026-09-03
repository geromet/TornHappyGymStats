#!/usr/bin/env bash
# remote-exec.sh — Run a script on the server without stealing sudo's stdin.
# shellcheck shell=bash
#
# Sourced, not executed. Expects DEPLOY_SSH_HOST / DEPLOY_SSH_USER /
# DEPLOY_SSH_KEY / DEPLOY_PROXY_COMMAND to already be set.
#
# THE BUG THIS EXISTS TO PREVENT
#
#   ssh -tt host 'bash -s' <<'EOF' ... EOF
#
#   looks obviously correct and is not. The script arrives on the remote's
#   STDIN, and sudo reads its PASSWORD from stdin — so the moment anything
#   inside the script calls sudo, sudo consumes the remaining script text line
#   by line as password guesses. The visible result is "sudo: 3 incorrect
#   password attempts", a transcript full of echoed shell source, and no work
#   done. It cost three separate debugging rounds across four scripts.
#
#   The fix is to send the script as a command ARGUMENT (base64-encoded so
#   quoting survives) and leave stdin attached to the operator's terminal, where
#   sudo can prompt and read a password normally.
#
#   A second, independent trap: sudo writes its prompt to stdout/stderr. Capturing
#   those with `> file 2>&1` or `$( ... )` swallows the prompt, so the operator
#   sees nothing while sudo waits forever. Anything that needs the output saved
#   must therefore tee rather than redirect, and must not filter — the prompt has
#   no trailing newline, so a line-buffered filter would hold it back and
#   reintroduce the hang.

if [[ -n "${_REMOTE_EXEC_LOADED:-}" ]]; then
  return 0
fi
readonly _REMOTE_EXEC_LOADED=1

# remote_exec_script [--tee FILE] [--indent] <<'EOF' ... EOF
#
#   --tee FILE   also save the session to FILE, cleaned of CRs and the sudo
#                prompt line
#   --indent     indent the terminal output by four spaces for readability
#
# Returns the remote script's exit status.
remote_exec_script() {
  local tee_file="" indent=0
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tee) tee_file="${2:-}"; shift 2 ;;
      --indent) indent=1; shift ;;
      *) echo "remote_exec_script: unknown option $1" >&2; return 2 ;;
    esac
  done

  local payload
  payload="$(base64 | tr -d '\n')"
  if [[ -z "${payload}" ]]; then
    echo "remote_exec_script: empty script on stdin" >&2
    return 2
  fi

  # Base64 is [A-Za-z0-9+/=] only, so single-quoting it on the remote command
  # line is safe with no escaping.
  local remote_cmd
  remote_cmd="_p='${payload}'; _s=\$(printf '%s' \"\$_p\" | base64 -d); bash -c \"\$_s\""

  local ssh_args=(
    -tt
    -i "${DEPLOY_SSH_KEY}"
    -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}"
    "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
    "${remote_cmd}"
  )

  local rc=0
  if [[ -n "${tee_file}" ]]; then
    if (( indent )); then
      # tee before any filter: the sudo prompt has no trailing newline and a
      # line-buffered sed would hold it back until the next newline arrived.
      ssh "${ssh_args[@]}" 2>&1 | tee "${tee_file}" | sed 's/^/    /'
    else
      ssh "${ssh_args[@]}" 2>&1 | tee "${tee_file}"
    fi
    rc="${PIPESTATUS[0]}"
    if [[ -s "${tee_file}" ]]; then
      sed -i 's/\r$//' "${tee_file}" 2>/dev/null || true
      sed -i '/^\[sudo\] password for /d' "${tee_file}" 2>/dev/null || true
    fi
  else
    ssh "${ssh_args[@]}" || rc=$?
  fi

  return "${rc}"
}
