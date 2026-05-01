#!/usr/bin/env bash
# deploy-backend.sh — Publish API and deploy to www.geromet.com over Cloudflare Access SSH.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"

# Optional local overrides file (never committed with secrets)
if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_REMOTE_ROOT:=/var/www/happygymstats}"
: "${DEPLOY_REMOTE_SERVICE:=happygymstats-api}"
: "${DEPLOY_CONFIGURATION:=Release}"
: "${DEPLOY_RUNTIME:=linux-x64}"
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=0}"
: "${DEPLOY_RESTART_SERVICE:=1}"

readonly PUBLISH_DIR="${ROOT_DIR}/dist/backend-api"
readonly REMOTE_RELEASES_DIR="${DEPLOY_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/happygymstats-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

SSH_OPTS=(
  -i "${DEPLOY_SSH_KEY}"
  -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}"
)

ssh_cmd_tty() {
  ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}

ssh_cmd_pipe() {
  ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}


echo "==> Publishing API"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${API_PROJECT}" \
  -c "${DEPLOY_CONFIGURATION}" \
  -r "${DEPLOY_RUNTIME}" \
  --self-contained true \
  -o "${PUBLISH_DIR}"

if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
  if [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]]; then
    SUDO_CMD="sudo -n"
  else
    SUDO_CMD="sudo"
  fi
else
  SUDO_CMD=""
fi

echo "==> Uploading payload (single SSH stream)"
tar -C "${PUBLISH_DIR}" -cf - . | ssh_cmd_pipe "set -euo pipefail; \
  mkdir -p '${REMOTE_STAGING_DIR}'; \
  rm -rf '${REMOTE_STAGING_DIR}'/*; \
  tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating release"
ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}'; \
  ${SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'; \
  ${SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'; \
  rm -rf '${REMOTE_STAGING_DIR}'"

if [[ "${DEPLOY_RESTART_SERVICE}" == "1" ]]; then
  echo "==> Restarting service ${DEPLOY_REMOTE_SERVICE}"
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} systemctl restart '${DEPLOY_REMOTE_SERVICE}'; \
    ${SUDO_CMD} systemctl --no-pager --full status '${DEPLOY_REMOTE_SERVICE}' | head -n 25"
else
  echo "==> Skipping service restart (DEPLOY_RESTART_SERVICE=${DEPLOY_RESTART_SERVICE})"
fi

echo "==> Deployment complete"
echo "    Host: ${DEPLOY_SSH_HOST}"
echo "    Release: ${REMOTE_RELEASE_DIR}"
echo "    Current symlink: ${REMOTE_CURRENT_DIR}"
