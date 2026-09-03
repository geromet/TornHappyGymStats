#!/usr/bin/env bash
# recon-fetch.sh — Run a read-only collector on the server and save the report.
#
# SCRIPT_CATEGORY=recon
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Run this yourself: the SSH tunnel is behind Cloudflare Access and needs an
# interactive passkey touch, so it has to happen in your own terminal.
#
# scripts/lib/recon-common.sh is concatenated with the collector locally and the
# combined script is piped over stdin, so nothing is ever written to the remote
# filesystem. Output lands in workspace/tmp/, which is gitignored — these
# reports are host configuration and should not be committed.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly LIB="${SCRIPT_DIR}/lib/recon-common.sh"
readonly OUT_DIR="${ROOT_DIR}/workspace/tmp"

usage() {
  cat <<'EOF'
Usage: bash scripts/recon-fetch.sh <collector> [--sudo] [--out PATH]

Collectors:
  devhost     scripts/recon-devhost.sh          dev-host bootstrap readiness
  ports       scripts/recon-ports.sh            listening ports, ownership, firewall
  security    scripts/audit-server-security.sh  system health and security review
  all         run every collector in turn

Options:
  --sudo      Run the collector as root on the remote (recommended).
              Without it, process ownership, firewall rules, sshd config and
              file modes cannot be read, and those are most of the answer —
              affected sections will print "BLIND — needs root".
  --out PATH  Write to a specific file instead of workspace/tmp/<name>-<UTC>.txt

Prerequisite:
  cloudflared access login https://ssh.geromet.com

With --sudo the report is streamed to your terminal as well as saved, so the
sudo password prompt is visible. Everything still lands in the output file.

Caching sudo in a separate session does not help: Ubuntu enables tty_tickets, so
a sudo timestamp is tied to the terminal that created it and a later ssh session
gets a different pty.

Read-only on both ends. No collector installs, starts, enables, reloads or
writes anything; every command is a query. Secrets are never printed —
environment files are reported as key names plus SET / PLACEHOLDER / EMPTY and a
keyed fingerprint (HMAC under a random per-run key that is never printed), so
values can be compared without being disclosed or guessed offline.
EOF
}

COLLECTOR_ARG=""
USE_SUDO=0
OUT_PATH=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --sudo) USE_SUDO=1; shift ;;
    --out) OUT_PATH="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; usage; exit 1 ;;
    *) COLLECTOR_ARG="$1"; shift ;;
  esac
done

if [[ -z "${COLLECTOR_ARG}" ]]; then
  usage
  exit 1
fi

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"

resolve_collector() {
  case "$1" in
    devhost)  printf '%s' "${SCRIPT_DIR}/recon-devhost.sh" ;;
    ports)    printf '%s' "${SCRIPT_DIR}/recon-ports.sh" ;;
    security) printf '%s' "${SCRIPT_DIR}/audit-server-security.sh" ;;
    *)        printf '%s' "$1" ;;
  esac
}

run_one() {
  local name="$1"
  local collector out
  collector="$(resolve_collector "${name}")"

  if [[ ! -f "${collector}" ]]; then
    echo "RECON_FAIL category=unknown_collector name=${name}" >&2
    return 1
  fi

  if [[ -n "${OUT_PATH}" ]]; then
    out="${OUT_PATH}"
  else
    mkdir -p "${OUT_DIR}"
    out="${OUT_DIR}/$(basename "${collector}" .sh)-$(date -u +%Y%m%dT%H%M%SZ).txt"
  fi

  echo "==> ${name}: ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
  echo "    collector: ${collector}"
  echo "    privilege: $( ((USE_SUDO)) && echo 'sudo (may prompt for your password)' || echo 'unprivileged — expect BLIND sections')"
  echo "    output:    ${out}"
  echo

  if (( USE_SUDO )); then
    # sudo reads its password from STDIN. Piping the script on stdin therefore
    # feeds the script to sudo as password guesses — which is exactly what
    # happened on the first attempt: "sudo: 3 incorrect password attempts",
    # a report full of echoed source, and no audit at all.
    #
    # So the script travels as a command ARGUMENT (base64, to survive quoting)
    # and stdin is left free for the human to type the password on the TTY.
    local payload
    payload="$(cat "${LIB}" "${collector}" | base64 | tr -d '\n')"
    local remote_cmd
    remote_cmd="_p='${payload}'; _s=\$(printf '%s' \"\$_p\" | base64 -d); sudo -p '[sudo] password for %u on %h: ' bash -c \"\$_s\""

    # sudo writes its prompt to the terminal through stdout/stderr. Redirecting
    # both straight into the report file swallows the prompt: sudo waits for a
    # password the operator was never shown, and the file ends up beginning with
    # "[sudo] password for ...". That is exactly what happened.
    #
    # tee keeps stdout attached to the terminal AND saves it. Note the prompt
    # has no trailing newline, so a line-buffered filter here would hold it back
    # until the next newline — tee is byte-oriented and passes it straight
    # through, which is why the output is not filtered.
    echo "    (the report streams past below; enter your sudo password when asked)"
    echo
    ssh -tt \
      -i "${DEPLOY_SSH_KEY}" \
      -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}" \
      "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" \
      "${remote_cmd}" 2>&1 | tee "${out}"
    echo

    # A forced TTY leaves CRs behind, and the prompt line is not part of the
    # report — drop both so the saved file is clean.
    if [[ -s "${out}" ]]; then
      sed -i 's/\r$//' "${out}" 2>/dev/null || true
      sed -i '1{/^\[sudo\] password for /d}' "${out}" 2>/dev/null || true
    fi
  else
    cat "${LIB}" "${collector}" | ssh -T \
      -i "${DEPLOY_SSH_KEY}" \
      -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}" \
      "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" \
      'bash -s' > "${out}" 2>&1 || true
  fi

  # Refuse to present a failed run as a report. The first --sudo attempt
  # produced 12KB of echoed shell source and zero findings; nothing downstream
  # noticed, because "the file is non-empty" was the only check.
  if grep -qE 'incorrect password attempt|Sorry, try again|is not in the sudoers file' "${out}" 2>/dev/null; then
    echo "RECON_FAIL category=sudo_auth_failed path=${out}" >&2
    echo "    sudo rejected the password, so no audit ran. The file contains the" >&2
    echo "    failed session, not a report. Re-run and enter the password when" >&2
    echo "    prompted, or configure passwordless sudo for this account." >&2
    return 1
  fi

  if ! head -8 "${out}" 2>/dev/null | grep -q '^\(HappyGymStats dev-host reconnaissance\|Listening-port investigation\|Server security and health audit\)'; then
    echo "RECON_FAIL category=no_report_header path=${out}" >&2
    echo "    Output does not begin with a collector header, so the collector did" >&2
    echo "    not run. Inspect the file — it holds the raw session, not a report." >&2
    return 1
  fi

  if [[ ! -s "${out}" ]]; then
    echo "RECON_FAIL category=empty_report path=${out}" >&2
    return 1
  fi

  echo "==> written: ${out} ($(wc -l < "${out}") lines)"

  if grep -q "Report complete" "${out}"; then
    if grep -q "BLIND — needs root" "${out}"; then
      echo "RECON_PARTIAL path=${out} detail=some_sections_need_root"
      echo "    Re-run with --sudo for a conclusive report."
    else
      echo "RECON_PASS path=${out}"
    fi
  else
    echo "RECON_PARTIAL path=${out} detail=no_end_marker" >&2
    echo "    The collector did not reach its end marker; check the tail of the file." >&2
  fi

  if grep -q "totals: " "${out}"; then
    echo "    $(grep 'totals: ' "${out}" | tail -1 | sed 's/^ *//')"
  fi
  echo
}

if [[ ! -f "${LIB}" ]]; then
  echo "RECON_FAIL category=missing_lib path=${LIB}" >&2
  exit 1
fi

if [[ "${COLLECTOR_ARG}" == "all" ]]; then
  if [[ -n "${OUT_PATH}" ]]; then
    echo "--out cannot be combined with 'all' (each collector needs its own file)." >&2
    exit 1
  fi
  rc=0
  for name in devhost ports security; do
    run_one "${name}" || rc=1
  done
else
  run_one "${COLLECTOR_ARG}" || exit 1
  rc=0
fi

cat <<EOF
Reports are in ${OUT_DIR} (gitignored).

Skim them before sharing — they are host configuration. The collectors are built
not to emit secret values, but you are the last check.
EOF

exit "${rc}"
