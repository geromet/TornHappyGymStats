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
#
#   A third: the prompt goes to STDERR, so the extremely ordinary
#
#       ${SUDO} docker ps -q 2>/dev/null
#
#   discards it too. sudo then waits for a password the operator was never
#   asked for, and the run hangs on a line that looks completely harmless.
#   There are dozens of such calls across these scripts, and auditing every one
#   forever is not a plan.
#
#   So SUDO_PREAMBLE below authenticates ONCE, up front, with the prompt
#   visible, and then pins SUDO to `sudo -n` for the rest of the script. `-n`
#   never prompts, so from that point on every `2>/dev/null` is harmless. Remote
#   scripts must use ${SUDO} and must NOT define it themselves.

if [[ -n "${_REMOTE_EXEC_LOADED:-}" ]]; then
  return 0
fi
readonly _REMOTE_EXEC_LOADED=1

# remote_exec_script [--tee FILE] [--indent] <<'EOF' ... EOF
#
#   --tee FILE   also save the session to FILE, cleaned of CRs and the sudo
#                prompt line
#   --indent     accepted and IGNORED. Indenting means filtering the stream,
#                and any line-buffered filter swallows sudo's newline-less
#                prompt. Kept only so existing call sites do not break.
#
# Returns the remote script's exit status.
remote_exec_script() {
  local tee_file=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tee) tee_file="${2:-}"; shift 2 ;;
      --indent) shift ;;   # ignored on purpose — see above
      *) echo "remote_exec_script: unknown option $1" >&2; return 2 ;;
    esac
  done

  local script preamble payload
  script="$(cat)"
  if [[ -z "${script}" ]]; then
    echo "remote_exec_script: empty script on stdin" >&2
    return 2
  fi

  # Authenticate sudo once, visibly, before the script runs. Everything after
  # uses `sudo -n`, which never prompts — so the many `2>/dev/null` calls in
  # these scripts can no longer swallow a password prompt.
  read -r -d '' preamble <<'PREAMBLE' || true
if [ "$(id -u)" = "0" ]; then
  SUDO=""
else
  if sudo -n true 2>/dev/null; then
    SUDO="sudo -n"
  else
    echo "This step needs administrator rights on the server."
    if sudo -v; then
      SUDO="sudo -n"
    else
      echo "REMOTE_SUDO_FAILED" >&2
      exit 77
    fi
  fi
fi
PREAMBLE

  payload="$(printf '%s\n%s\n' "${preamble}" "${script}" | base64 | tr -d '\n')"

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
    # NOTHING may sit between ssh and the terminal except tee.
    #
    # An earlier version piped through `sed 's/^/    /'` to indent the output,
    # directly under a comment warning that a line-buffered filter would swallow
    # the prompt. It did exactly that: the preamble's echo has a trailing
    # newline and appeared, the `sudo -v` prompt does not and never did, so the
    # run hung with no visible question. tee is byte-oriented and passes a
    # partial line straight through; sed cannot emit one until a newline
    # arrives, and `sed -u` does not help because the substitution is still
    # per-line. Cosmetic indentation is not worth a hang.
    ssh "${ssh_args[@]}" 2>&1 | tee "${tee_file}"
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
