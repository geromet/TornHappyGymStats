#!/usr/bin/env bash
# remote-exec.sh — Run a script on the server with working, safe sudo.
# shellcheck shell=bash
#
# Sourced, not executed. Expects DEPLOY_SSH_HOST / DEPLOY_SSH_USER /
# DEPLOY_SSH_KEY / DEPLOY_PROXY_COMMAND to be set.
#
# ── FIVE TRAPS, ALL HIT IN PRACTICE ─────────────────────────────────────────
#
# 1. Script on stdin.  ssh -tt host 'bash -s' <<'EOF_REMOTE' sends the script to the
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
#    stdout on a pipe the terminal is not handed to ssh cleanly.
#
# 5. STDIN IS THE HEREDOC.  This is the one that actually caused the password to
#    appear in clear text, and it is specific to taking the script on stdin.
#    Callers write:
#
#        remote_exec_script --tee X <<REMOTE ... REMOTE
#
#    so the heredoc becomes the FUNCTION's stdin, `cat` drains it, and ssh then
#    inherits an exhausted pipe. ssh only puts the local terminal into raw mode
#    when its stdin is a tty; with a pipe it stays in cooked mode, so the local
#    terminal echoes every keystroke and line-edits the paste. The remote sudo
#    dutifully disables echo on the REMOTE pty, which is not where the echo was
#    coming from.
#
#    The repo's own deploy-config.sh never hit this because it passes commands
#    as ARGUMENTS with no heredoc, leaving ssh's stdin attached to the terminal.
#    Fix: reconnect ssh's stdin to the controlling terminal explicitly.
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
# ── TEST TRANSPORT SEAM ─────────────────────────────────────────────────────
# Production still requires DEPLOY_PROXY_COMMAND. The only proxy bypass is the
# explicit HAPPYGYMSTATS_REMOTE_EXEC_TEST_DIRECT=1 seam, and it is accepted only
# for loopback hosts. This lets the disposable SSH/PTTY fixture exercise this
# exact transport without Cloudflare while making accidental production bypass
# fail closed. DEPLOY_SSH_PORT and DEPLOY_SSH_KNOWN_HOSTS_FILE are ordinary SSH
# options; both are optional and preserve current defaults when unset.
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

_remote_exec_validate_transport() {
  if [[ -z "${DEPLOY_SSH_HOST:-}" || -z "${DEPLOY_SSH_USER:-}" || -z "${DEPLOY_SSH_KEY:-}" ]]; then
    echo "remote_exec_script: DEPLOY_SSH_HOST, DEPLOY_SSH_USER and DEPLOY_SSH_KEY are required." >&2
    return 2
  fi

  if [[ -z "${DEPLOY_PROXY_COMMAND:-}" ]]; then
    if [[ "${HAPPYGYMSTATS_REMOTE_EXEC_TEST_DIRECT:-0}" != "1" ]]; then
      echo "remote_exec_script: DEPLOY_PROXY_COMMAND is required outside the disposable loopback test fixture." >&2
      return 2
    fi

    case "${DEPLOY_SSH_HOST}" in
      127.0.0.1|localhost|::1) ;;
      *)
        echo "remote_exec_script: direct SSH test seam is restricted to loopback hosts." >&2
        return 2
        ;;
    esac
  fi

  local port="${DEPLOY_SSH_PORT:-22}"
  if [[ ! "${port}" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
    echo "remote_exec_script: DEPLOY_SSH_PORT must be an integer from 1 to 65535." >&2
    return 2
  fi
}

_remote_exec_cleanup() {
  ssh -O exit -p "${DEPLOY_SSH_PORT:-22}" -o "ControlPath=${_remote_exec_ctl_path}" \
      "${DEPLOY_SSH_USER:-nobody}@${DEPLOY_SSH_HOST:-127.0.0.1}" >/dev/null 2>&1 || true
  rm -rf "${_remote_exec_ctl_dir}" 2>/dev/null || true
}
trap _remote_exec_cleanup EXIT

_remote_exec_ssh_opts() {
  printf '%s\n' \
    -i "${DEPLOY_SSH_KEY}" \
    -p "${DEPLOY_SSH_PORT:-22}"

  if [[ -n "${DEPLOY_PROXY_COMMAND:-}" ]]; then
    printf '%s\n' -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}"
  fi

  if [[ -n "${DEPLOY_SSH_KNOWN_HOSTS_FILE:-}" ]]; then
    printf '%s\n' -o "UserKnownHostsFile=${DEPLOY_SSH_KNOWN_HOSTS_FILE}"
  fi

  printf '%s\n' \
    -o "ControlMaster=auto" \
    -o "ControlPath=${_remote_exec_ctl_path}" \
    -o "ControlPersist=120"
}

# remote_exec_script [--tee FILE] [--indent] <<'EOF_REMOTE' ... EOF_REMOTE
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

  _remote_exec_validate_transport || return $?

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

  # UNPIPED on purpose (trap 4). Never add | tee, | sed, > file or $( ) here.
  #
  # `< /dev/tty` is load-bearing (trap 5): without it ssh inherits the drained
  # heredoc as stdin, never enters raw mode, and the sudo password is echoed in
  # clear text locally.
  if [[ -e /dev/tty ]] && { : >/dev/tty; } 2>/dev/null; then
    ssh -tt "${opts[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "${remote_cmd}" < /dev/tty || rc=$?
  else
    # Fail closed before starting SSH. A remote PTY is not a substitute for a
    # local controlling terminal: sudo could still block on an invisible prompt
    # that no process can answer, leaving CI/pipelines hung indefinitely.
    echo "remote_exec_script: no controlling terminal; interactive sudo is not possible." >&2
    echo "  Run this from an interactive shell, or configure passwordless sudo on the server." >&2
    return 77
  fi

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
