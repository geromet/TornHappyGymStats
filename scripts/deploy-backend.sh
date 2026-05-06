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

# API production runtime contract (names only; values remain server-local):
# - ConnectionStrings__HappyGymStats OR HAPPYGYMSTATS_CONNECTION_STRING
# - ProvisionalToken__SigningKey
# - HAPPYGYMSTATS_SURFACES_CACHE_DIR
# - ASPNETCORE_ENVIRONMENT
# - ASPNETCORE_URLS
readonly REMOTE_API_ENV_FILE="/etc/happygymstats/api.env"

echo "==> Precheck: API runtime contract"
ssh_cmd_tty "set -euo pipefail
  if [[ ! -f '${REMOTE_API_ENV_FILE}' ]]; then
    echo 'DEPLOY_PRECHECK_FAIL: missing_env_file path=${REMOTE_API_ENV_FILE}' >&2
    exit 20
  fi

  if ! grep -Eq '^(ConnectionStrings__HappyGymStats|HAPPYGYMSTATS_CONNECTION_STRING)=' '${REMOTE_API_ENV_FILE}'; then
    echo 'DEPLOY_PRECHECK_FAIL: missing_env_var ConnectionStrings__HappyGymStats_or_HAPPYGYMSTATS_CONNECTION_STRING' >&2
    exit 21
  fi

  for key in ProvisionalToken__SigningKey HAPPYGYMSTATS_SURFACES_CACHE_DIR ASPNETCORE_ENVIRONMENT ASPNETCORE_URLS; do
    if ! grep -Eq "^${key}=" '${REMOTE_API_ENV_FILE}'; then
      echo "DEPLOY_PRECHECK_FAIL: missing_env_var ${key}" >&2
      exit 22
    fi
  done

  echo 'DEPLOY_PRECHECK_OK: api_env_contract'
"

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

run_backend_health_gates() {
  if [[ "${DEPLOY_API_HEALTH_GATES}" != "1" ]]; then
    echo "==> Health gates disabled (DEPLOY_API_HEALTH_GATES=${DEPLOY_API_HEALTH_GATES})"
    return 0
  fi

  echo "==> Health gate: service status (${DEPLOY_API_SERVICE})"
  ssh_cmd_tty "set -euo pipefail
    service='${DEPLOY_API_SERVICE}'
    if ! ${SUDO_CMD} systemctl is-active --quiet \"${DEPLOY_API_SERVICE}\"; then
      echo \"DEPLOY_HEALTH_FAIL: category=service_inactive service=${DEPLOY_API_SERVICE}\" >&2
      ${SUDO_CMD} systemctl status --no-pager --full \"${DEPLOY_API_SERVICE}\" >&2 || true
      exit 30
    fi
    echo \"DEPLOY_HEALTH_OK: category=service_active service=${DEPLOY_API_SERVICE}\"
  "

  echo "==> Health gate: loopback API (${DEPLOY_API_LOOPBACK_HEALTH_URL})"
  ssh_cmd_tty "set -euo pipefail
    url='${DEPLOY_API_LOOPBACK_HEALTH_URL}'
    timeout='${DEPLOY_API_HEALTH_TIMEOUT_SECONDS}'
    body_max='${DEPLOY_API_HEALTH_BODY_MAX_BYTES}'
    body_file=\"/tmp/happygymstats-loopback-health-${DEPLOY_SSH_USER}.body\"
    code_file=\"/tmp/happygymstats-loopback-health-${DEPLOY_SSH_USER}.code\"
    rm -f \"\${body_file}\" \"\${code_file}\"

    curl_status=0
    if ! curl -sS --max-time \"\${timeout}\" -o \"\${body_file}\" -w '%{http_code}' \"\${url}\" > \"\${code_file}\"; then
      curl_status=$?
      echo \"DEPLOY_HEALTH_FAIL: category=loopback_unreachable url=${DEPLOY_API_LOOPBACK_HEALTH_URL} curl_exit=\${curl_status}\" >&2
      rm -f \"\${body_file}\" \"\${code_file}\"
      exit 31
    fi

    status_code=\"\$(tr -d '[:space:]' < \"\${code_file}\")\"
    if [[ ! \"\${status_code}\" =~ ^2 ]]; then
      body_excerpt=\"\$(head -c \"\${body_max}\" \"\${body_file}\" | tr '\\n' ' ' | tr '\\r' ' ')\"
      echo \"DEPLOY_HEALTH_FAIL: category=loopback_non_2xx url=${DEPLOY_API_LOOPBACK_HEALTH_URL} status=\${status_code} body='\${body_excerpt}'\" >&2
      rm -f \"\${body_file}\" \"\${code_file}\"
      exit 32
    fi

    echo \"DEPLOY_HEALTH_OK: category=loopback_2xx url=${DEPLOY_API_LOOPBACK_HEALTH_URL} status=\${status_code}\"
    rm -f \"\${body_file}\" \"\${code_file}\"
  "

  echo "==> Health gate: external API (${DEPLOY_API_EXTERNAL_HEALTH_URL})"
  ssh_cmd_tty "set -euo pipefail
    url='${DEPLOY_API_EXTERNAL_HEALTH_URL}'
    timeout='${DEPLOY_API_HEALTH_TIMEOUT_SECONDS}'
    body_max='${DEPLOY_API_HEALTH_BODY_MAX_BYTES}'
    body_file=\"/tmp/happygymstats-external-health-${DEPLOY_SSH_USER}.body\"
    code_file=\"/tmp/happygymstats-external-health-${DEPLOY_SSH_USER}.code\"
    rm -f \"\${body_file}\" \"\${code_file}\"

    curl_status=0
    if ! curl -sS --max-time \"\${timeout}\" -o \"\${body_file}\" -w '%{http_code}' \"\${url}\" > \"\${code_file}\"; then
      curl_status=$?
      echo \"DEPLOY_HEALTH_FAIL: category=external_unreachable url=${DEPLOY_API_EXTERNAL_HEALTH_URL} curl_exit=\${curl_status}\" >&2
      rm -f \"\${body_file}\" \"\${code_file}\"
      exit 33
    fi

    status_code=\"\$(tr -d '[:space:]' < \"\${code_file}\")\"
    if [[ \"\${status_code}\" == \"502\" ]]; then
      body_excerpt=\"\$(head -c \"\${body_max}\" \"\${body_file}\" | tr '\\n' ' ' | tr '\\r' ' ')\"
      echo \"DEPLOY_HEALTH_FAIL: category=external_nginx_502 url=${DEPLOY_API_EXTERNAL_HEALTH_URL} status=\${status_code} body='\${body_excerpt}'\" >&2
      rm -f \"\${body_file}\" \"\${code_file}\"
      exit 34
    fi

    if [[ ! \"\${status_code}\" =~ ^2 ]]; then
      body_excerpt=\"\$(head -c \"\${body_max}\" \"\${body_file}\" | tr '\\n' ' ' | tr '\\r' ' ')\"
      echo \"DEPLOY_HEALTH_FAIL: category=external_non_2xx url=${DEPLOY_API_EXTERNAL_HEALTH_URL} status=\${status_code} body='\${body_excerpt}'\" >&2
      rm -f \"\${body_file}\" \"\${code_file}\"
      exit 35
    fi

    echo \"DEPLOY_HEALTH_OK: category=external_2xx url=${DEPLOY_API_EXTERNAL_HEALTH_URL} status=\${status_code}\"
    rm -f \"\${body_file}\" \"\${code_file}\"
  "
}

if [[ "${DEPLOY_API_RESTART}" == "1" ]]; then
  echo "==> Restarting ${DEPLOY_API_SERVICE}"
  ssh_cmd_tty "${SUDO_CMD} systemctl restart '${DEPLOY_API_SERVICE}'"
fi

run_backend_health_gates

echo "==> Backend deployment complete — ${REMOTE_RELEASE_DIR}"
