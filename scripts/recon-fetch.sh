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

If --sudo prompts and the prompt does not appear correctly, run `sudo -v` on the
host first, then re-run without --sudo: the collectors detect passwordless or
recently-cached sudo on their own.

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

  local remote_cmd ssh_flags
  if (( USE_SUDO )); then
    remote_cmd='sudo -p "[sudo] password for %u on %h: " bash -s'
    ssh_flags="-tt"   # sudo needs a TTY to prompt while the script arrives on stdin
  else
    remote_cmd='bash -s'
    ssh_flags="-T"
  fi

  # shellcheck disable=SC2086
  if cat "${LIB}" "${collector}" | ssh ${ssh_flags} \
      -i "${DEPLOY_SSH_KEY}" \
      -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}" \
      "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" \
      "${remote_cmd}" > "${out}" 2>&1; then
    :
  fi

  # Reports carry \r when a TTY was forced; strip so they read cleanly.
  if (( USE_SUDO )) && [[ -s "${out}" ]]; then
    sed -i 's/\r$//' "${out}" 2>/dev/null || true
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
