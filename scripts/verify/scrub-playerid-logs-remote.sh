#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# Optional shared deploy config (non-fatal if missing).
readonly DEPLOY_CONFIG_PATH="${ROOT_DIR}/scripts/deploy-config.sh"
if [[ -f "${DEPLOY_CONFIG_PATH}" ]]; then
  # shellcheck disable=SC1090
  source "${DEPLOY_CONFIG_PATH}"
fi

: "${SCRUB_SSH_HOST:=ssh.geromet.com}"
: "${SCRUB_SSH_USER:=anon}"
: "${SCRUB_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${SCRUB_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${SCRUB_REMOTE_PATH:=/tmp/scrub-playerid-logs-payload.sh}"
: "${SCRUB_JOURNAL_SERVICE:=happygymstats-api}"
: "${SCRUB_JOURNAL_VACUUM_TIME:=7d}"

readonly PAYLOAD_LOCAL_PATH="${SCRIPT_DIR}/scrub-playerid-logs-payload.sh"

usage() {
  cat <<EOF
Usage: bash scripts/verify/scrub-playerid-logs-remote.sh --yes [--help]

SCRIPT_CATEGORY=ops-mutating-upload
SCRIPT_MUTATES_SERVER_STATE=0
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Uploads scrub payload to remote host. Does not mutate logs by itself.
After upload, run payload interactively on host so sudo prompt works.

Required flag:
  --yes    Confirm upload action.

Environment overrides:
  SCRUB_SSH_HOST          SSH host (default: ${SCRUB_SSH_HOST})
  SCRUB_SSH_USER          SSH user (default: ${SCRUB_SSH_USER})
  SCRUB_SSH_KEY           SSH private key path (default: ${SCRUB_SSH_KEY})
  SCRUB_PROXY_COMMAND     SSH ProxyCommand (default: ${SCRUB_PROXY_COMMAND})
  SCRUB_REMOTE_PATH       Remote payload path (default: ${SCRUB_REMOTE_PATH})
  SCRUB_JOURNAL_SERVICE   Passed to payload at run time (default: ${SCRUB_JOURNAL_SERVICE})
  SCRUB_JOURNAL_VACUUM_TIME  Passed to payload at run time (default: ${SCRUB_JOURNAL_VACUUM_TIME})
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ "${1:-}" != "--yes" ]]; then
  echo "ERROR: This uploads an ops payload to remote host. Re-run with --yes to confirm." >&2
  exit 2
fi

if [[ ! -f "${PAYLOAD_LOCAL_PATH}" ]]; then
  echo "ERROR: payload not found: ${PAYLOAD_LOCAL_PATH}" >&2
  exit 3
fi

echo "==> Uploading payload to ${SCRUB_SSH_USER}@${SCRUB_SSH_HOST}:${SCRUB_REMOTE_PATH}"
scp -i "${SCRUB_SSH_KEY}" \
  -o "ProxyCommand=${SCRUB_PROXY_COMMAND}" \
  "${PAYLOAD_LOCAL_PATH}" \
  "${SCRUB_SSH_USER}@${SCRUB_SSH_HOST}:${SCRUB_REMOTE_PATH}"

echo "==> Marking remote payload executable"
ssh -i "${SCRUB_SSH_KEY}" \
  -o "ProxyCommand=${SCRUB_PROXY_COMMAND}" \
  "${SCRUB_SSH_USER}@${SCRUB_SSH_HOST}" \
  "chmod +x '${SCRUB_REMOTE_PATH}'"

cat <<EOF

Upload complete.

Next steps (interactive, on host):

1) SSH into host:
ssh -i "${SCRUB_SSH_KEY}" -o "ProxyCommand=${SCRUB_PROXY_COMMAND}" ${SCRUB_SSH_USER}@${SCRUB_SSH_HOST}

2) Run payload:
SCRUB_JOURNAL_SERVICE='${SCRUB_JOURNAL_SERVICE}' \\
SCRUB_JOURNAL_VACUUM_TIME='${SCRUB_JOURNAL_VACUUM_TIME}' \\
'${SCRUB_REMOTE_PATH}' --yes

EOF
