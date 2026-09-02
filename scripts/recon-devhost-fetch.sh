#!/usr/bin/env bash
# recon-devhost-fetch.sh — Run scripts/recon-devhost.sh on the server and save
# the report under workspace/tmp/ for review.
#
# SCRIPT_CATEGORY=recon
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Run this yourself: the SSH tunnel is behind Cloudflare Access and needs an
# interactive passkey touch, so it has to happen in your own terminal.
#
# The collector is piped over stdin rather than copied to the server, so nothing
# is written to the remote filesystem. Output lands in workspace/tmp/, which is
# gitignored — the report is host configuration and should not be committed.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly COLLECTOR="${SCRIPT_DIR}/recon-devhost.sh"
readonly OUT_DIR="${ROOT_DIR}/workspace/tmp"

usage() {
  cat <<EOF
Usage: bash scripts/recon-devhost-fetch.sh [--out PATH]

Runs scripts/recon-devhost.sh on the deploy host over SSH and writes the report
to workspace/tmp/devhost-recon-<UTC timestamp>.txt (override with --out).

Read-only on both ends. The collector never prints a secret value; environment
files are reported as key names plus SET / PLACEHOLDER / EMPTY and a truncated
SHA-256 so dev and production secrets can be compared without being disclosed.

Prerequisite:
  cloudflared access login https://ssh.geromet.com

Environment overrides (shared with the deploy scripts):
  DEPLOY_SSH_HOST       (default: ssh.geromet.com)
  DEPLOY_SSH_USER       (default: anon)
  DEPLOY_SSH_KEY        (default: ~/.ssh/id_token2_bio3_hetzner)
  DEPLOY_PROXY_COMMAND  (default: cloudflared access ssh --hostname ssh.geromet.com)
EOF
}

OUT_PATH=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT_PATH="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"

if [[ ! -f "${COLLECTOR}" ]]; then
  echo "RECON_FAIL category=missing_collector path=${COLLECTOR}" >&2
  exit 1
fi

if [[ -z "${OUT_PATH}" ]]; then
  mkdir -p "${OUT_DIR}"
  OUT_PATH="${OUT_DIR}/devhost-recon-$(date -u +%Y%m%dT%H%M%SZ).txt"
fi

echo "==> Running read-only recon on ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
echo "    collector: ${COLLECTOR} (piped over stdin; nothing written on the remote)"
echo "    output:    ${OUT_PATH}"
echo
echo "    Cloudflare Access may prompt for your passkey now."
echo

if ssh -T \
    -i "${DEPLOY_SSH_KEY}" \
    -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}" \
    "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" \
    'bash -s' < "${COLLECTOR}" > "${OUT_PATH}" 2>&1; then
  status="ok"
else
  status="ssh_or_collector_error"
fi

echo
if [[ ! -s "${OUT_PATH}" ]]; then
  echo "RECON_FAIL category=empty_report path=${OUT_PATH} detail=${status}" >&2
  exit 1
fi

echo "==> Report written: ${OUT_PATH}"
echo "    $(wc -l < "${OUT_PATH}") lines, $(du -h "${OUT_PATH}" | cut -f1)"

if grep -q "Report complete" "${OUT_PATH}"; then
  echo "RECON_PASS path=${OUT_PATH}"
else
  echo "RECON_PARTIAL path=${OUT_PATH} detail=${status}" >&2
  echo "    The collector did not reach its end marker; the report may be truncated." >&2
  echo "    Check the tail of the file for the failure." >&2
fi

cat <<EOF

Before sharing this report, skim it once — it is host configuration. The
collector is built not to emit secret values, but you are the last check.
EOF
