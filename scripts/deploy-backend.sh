#!/usr/bin/env bash
# deploy-backend.sh — Publish and deploy the API to torn.geromet.com.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"
readonly PUBLISH_DIR="${ROOT_DIR}/dist/backend-api"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: bash scripts/deploy-backend.sh"
  exit 0
fi

[[ -f "${ROOT_DIR}/.env.deploy" ]] && source "${ROOT_DIR}/.env.deploy"
source "${SCRIPT_DIR}/deploy-config.sh"

readonly REMOTE_TS="$(date -u +%Y%m%dT%H%M%SZ)"
readonly REMOTE_RELEASES_DIR="${DEPLOY_API_REMOTE_ROOT}/releases"
readonly REMOTE_CURRENT_DIR="${DEPLOY_API_REMOTE_ROOT}/current"
readonly REMOTE_STAGING_DIR="/tmp/happygymstats-api-staging-${DEPLOY_SSH_USER}"
readonly REMOTE_RELEASE_DIR="${REMOTE_RELEASES_DIR}/${REMOTE_TS}"

echo "==> Publishing API"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${API_PROJECT}" -c "${DEPLOY_CONFIGURATION}" -r "${DEPLOY_RUNTIME}" --self-contained true -o "${PUBLISH_DIR}"

echo "==> Uploading payload"
tar -C "${PUBLISH_DIR}" -cf - . | ssh_cmd_pipe "set -euo pipefail
  mkdir -p '${REMOTE_STAGING_DIR}'
  rm -rf '${REMOTE_STAGING_DIR}'/*
  tar -xf - -C '${REMOTE_STAGING_DIR}'"

echo "==> Activating release"
ssh_cmd_tty "set -euo pipefail
  ${SUDO_CMD} mkdir -p '${REMOTE_RELEASES_DIR}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_API_REMOTE_ROOT}/data'
  ${SUDO_CMD} rsync -a --delete '${REMOTE_STAGING_DIR}/' '${REMOTE_RELEASE_DIR}/'
  if [[ -d '${REMOTE_CURRENT_DIR}' && ! -L '${REMOTE_CURRENT_DIR}' ]]; then ${SUDO_CMD} rm -rf '${REMOTE_CURRENT_DIR}'; fi
  ${SUDO_CMD} ln -sfn '${REMOTE_RELEASE_DIR}' '${REMOTE_CURRENT_DIR}'
  ${SUDO_CMD} chown -R '${DEPLOY_API_OWNER}:${DEPLOY_API_GROUP}' '${REMOTE_RELEASE_DIR}' '${DEPLOY_API_REMOTE_ROOT}/data'
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type d -exec chmod 755 {} \\;
  ${SUDO_CMD} find '${REMOTE_RELEASE_DIR}' -type f -exec chmod 644 {} \\;
  [[ -f '${REMOTE_RELEASE_DIR}/HappyGymStats.Api' ]] && ${SUDO_CMD} chmod 755 '${REMOTE_RELEASE_DIR}/HappyGymStats.Api'
  rm -rf '${REMOTE_STAGING_DIR}'"

if [[ "${DEPLOY_API_RESTART}" == "1" ]]; then
  echo "==> Restarting ${DEPLOY_API_SERVICE}"
  ssh_cmd_tty "${SUDO_CMD} systemctl restart '${DEPLOY_API_SERVICE}'"
fi

echo "==> Backend deployment complete — ${REMOTE_RELEASE_DIR}"
