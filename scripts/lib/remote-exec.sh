#!/usr/bin/env bash
# remote-exec.sh — Run a script on the server with working, safe sudo.
# shellcheck shell=bash
#
# Sourced, not executed. Expects DEPLOY_SSH_HOST / DEPLOY_SSH_USER /
# DEPLOY_SSH_KEY / DEPLOY_PROXY_COMMAND to be set.
#
# ── FOUR TRAPS, ALL HIT IN PRACTICE ─────────────────────────────────────────
#
# 1. Script on stdin.  ssh -tt host 'bash -s' <<'EOF' sends the script to the
#    remote's STDIN, and sudo reads its PASSWORD from stdin — so sudo eats the
#    script line by line as password guesses. Fix: send the script as a base64
#    command ARGUMENT and leave stdin alone.
#
# 2. Captured prompt.  sudo writes its prompt to stdout/stderr, so > file 2>&1
#    or $( ... ) swallows it and the operator waits on an invisible question.
#
# 3. Filtered prompt.  The prompt has no trailing newline, so ANY line-buffered
#    filter (| sed 's/^/  /') holds it until EOF — and sudo never reaches EOF
#    while waiting. Measured: visible after 3.4s with sed, 0.3s without.
#
# 4. Piped stdout breaks the tty.  Even ssh -tt ... | tee file misbehaves: with
#    stdout on a pipe the terminal is not handed to ssh cleanly, the typed
#    password is ECHOED IN CLEAR and can arrive truncated. Unacceptable for a
#    password.
#
# So the interactive session is NEVER piped. ssh owns the terminal completely,
# exactly as a normal login would, and sudo suppresses echo the way it should.
# Output is captured by teeing on the REMOTE side to a temp file, which a second
# multiplexed connection retrieves and deletes. ControlMaster shares the
# connection, so the key passphrase and Cloudflare Access are prompted once.
#
# That temp file is the only thing written on the server: /tmp, random name,
# removed as soon as it is read.
#
# ── RULE FOR CALLERS ────────────────────────────────────────────────────────
# The heredoc is UNQUOTED in most callers so local variables interpolate. That
# means the LOCAL shell also expands $(...) and BACKTICKS inside it. A comment
# containing sudo -n in backticks was executed locally and printed a sudo usage
# error; another containing an "or echo 0" phrase in backticks caused a syntax
# error. Never put backticks in an unquoted heredoc.

if [[ -n "${_REMOTE_EXEC_LOADED:-}" ]]; then
  return 0
fi
readonly _REMOTE_EXEC_LOADED=1

_remote_exec_ctl_dir="${TMPDIR:-/tmp}/happygymstats-ssh.$$"
mkdir -p "${_remote_exec_ctl_dir}" 2>/dev/null || true
_remote_exec_ctl_path="${_remote_exec_ctl_dir}/cm-%r@%h:%p"

_remote_exec_cleanup() {
  ssh -O exit -o "ControlPath=${_remote_exec_ctl_path}" \
      "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" >/dev/null 2>&1 || true
  rm -rf "${_remote_exec_ctl_dir}" 2>/dev/null || true
}
trap _remote_exec_cleanup EXIT

_remote_exec_ssh_opts() {
  printf '%s\n' \
    -i "${DEPLOY_SSH_KEY}" \
    -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}" \
    -o "ControlMaster=auto" \
    -o "ControlPath=${_remote_exec_ctl_path}" \
    -o "ControlPersist=120"
}

# remote_exec_script [--tee FILE] [--indent] <<'EOF' ... EOF
#
#   --tee FILE   save the session transcript to FILE
#   --indent     accepted and ignored (see trap 3 — indenting means filtering)
#
# Returns the remote script's exit status.
remote_exec_script() {
  local tee_file=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tee) tee_file="${2:-}"; shift 2 ;;
      --indent) shift ;;
      *) echo "remote_exec_script: unknown option $1" >&2; return 2 ;;
    esac
  done

  local script
  script="$(cat)"
  if [[ -z "${script}" ]]; then
    echo "remote_exec_script: empty script on stdin" >&2
    return 2
  fi

  # Authenticate sudo once, up front, where the prompt is visible; then pin SUDO
  # to sudo -n so the many 2>/dev/null calls downstream cannot swallow a prompt
  # (trap 2, in a form that is impossible to audit call by call).
  local preamble
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

  local payload remote_log rc=0
  payload="$(printf '%s\n%s\n' "${preamble}" "${script}" | base64 | tr -d '\n')"
  remote_log="/tmp/hgs-exec-$$-${RANDOM}.log"

  local -a opts
  mapfile -t opts < <(_remote_exec_ssh_opts)

  local remote_cmd
  if [[ -n "${tee_file}" ]]; then
    # tee on the REMOTE side so the local terminal stays unpiped (trap 4).
    remote_cmd="_p='${payload}'; _s=\$(printf '%s' \"\$_p\" | base64 -d); { bash -c \"\$_s\"; } 2>&1 | tee '${remote_log}'; exit \${PIPESTATUS[0]}"
  else
    remote_cmd="_p='${payload}'; _s=\$(printf '%s' \"\$_p\" | base64 -d); bash -c \"\$_s\""
  fi

  # UNPIPED on purpose. Never add | tee, | sed, > file or $( ) around this line.
  ssh -tt "${opts[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "${remote_cmd}" || rc=$?

  if [[ -n "${tee_file}" ]]; then
    # Reuses the multiplexed connection, needs no sudo, removes the temp file.
    ssh -T "${opts[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" \
        "cat '${remote_log}' 2>/dev/null; rm -f '${remote_log}'" > "${tee_file}" 2>/dev/null || true
    if [[ -s "${tee_file}" ]]; then
      sed -i 's/\r$//' "${tee_file}" 2>/dev/null || true
      sed -i '/^\[sudo\] password for /d' "${tee_file}" 2>/dev/null || true
    fi
  fi

  return "${rc}"
}
