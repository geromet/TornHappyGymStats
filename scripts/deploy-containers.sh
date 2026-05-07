#!/usr/bin/env bash
# deploy-containers.sh — Deploy container stack over SSH using shared deploy config.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DEPLOY_CONFIG_PATH="${SCRIPT_DIR}/deploy-config.sh"

if [[ ! -f "${DEPLOY_CONFIG_PATH}" ]]; then
  echo "DEPLOY_CONFIG_MISSING path=${DEPLOY_CONFIG_PATH}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${DEPLOY_CONFIG_PATH}"

: "${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE:=${ROOT_DIR}/infra/docker-compose.yml}"
: "${DEPLOY_CONTAINERS_REMOTE_ROOT:=/opt/happygymstats/containers}"
: "${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE:=docker-compose.yml}"
: "${DEPLOY_CONTAINERS_REMOTE_ENV_FILE:=.env.production}"
: "${DEPLOY_CONTAINERS_REMOTE_STACK_NAME:=happygymstats}"
: "${DEPLOY_CONTAINERS_PULL:=1}"
: "${DEPLOY_CONTAINERS_UP_FLAGS:=--detach --remove-orphans}"

usage() {
  cat <<EOF
Usage: bash scripts/deploy-containers.sh [--help]

Deploys the container compose stack by uploading compose config and running
remote docker compose pull/up with shared SSH/config conventions.

Required local preconditions:
  - ${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE} exists

Required remote preconditions (name-only, no secret values):
  - docker and docker compose plugin available
  - DEPLOY_CONTAINERS_REMOTE_ROOT writable (or writable via sudo)
  - ${DEPLOY_CONTAINERS_REMOTE_ROOT}/${DEPLOY_CONTAINERS_REMOTE_ENV_FILE} exists
  - Remote env file defines required app secrets/connection settings used by compose

Config variables:
  DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE   (default: ${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE})
  DEPLOY_CONTAINERS_REMOTE_ROOT          (default: ${DEPLOY_CONTAINERS_REMOTE_ROOT})
  DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE  (default: ${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE})
  DEPLOY_CONTAINERS_REMOTE_ENV_FILE      (default: ${DEPLOY_CONTAINERS_REMOTE_ENV_FILE})
  DEPLOY_CONTAINERS_REMOTE_STACK_NAME    (default: ${DEPLOY_CONTAINERS_REMOTE_STACK_NAME})
  DEPLOY_CONTAINERS_PULL                 (default: ${DEPLOY_CONTAINERS_PULL})
  DEPLOY_CONTAINERS_UP_FLAGS             (default: ${DEPLOY_CONTAINERS_UP_FLAGS})

EOF
  deploy_print_common_connection_summary
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ ! -f "${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE}" ]]; then
  echo "DEPLOY_PRECHECK_FAIL missing_local_compose=${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE}" >&2
  exit 1
fi

readonly REMOTE_COMPOSE_PATH="${DEPLOY_CONTAINERS_REMOTE_ROOT}/${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE}"
readonly REMOTE_ENV_PATH="${DEPLOY_CONTAINERS_REMOTE_ROOT}/${DEPLOY_CONTAINERS_REMOTE_ENV_FILE}"

echo "==> Uploading compose file"
cat "${DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE}" | deploy_ssh_pipe "set -euo pipefail; ${DEPLOY_SUDO_CMD} mkdir -p '${DEPLOY_CONTAINERS_REMOTE_ROOT}'; cat > '${REMOTE_COMPOSE_PATH}'"

echo "==> Checking remote container deploy preconditions"
deploy_ssh_tty "set -euo pipefail; \
  command -v docker >/dev/null; \
  docker compose version >/dev/null; \
  test -f '${REMOTE_ENV_PATH}'"

echo "==> Deploying container stack"
if [[ "${DEPLOY_CONTAINERS_PULL}" == "1" ]]; then
  deploy_ssh_tty "set -euo pipefail; \
    cd '${DEPLOY_CONTAINERS_REMOTE_ROOT}'; \
    docker compose --env-file '${DEPLOY_CONTAINERS_REMOTE_ENV_FILE}' -p '${DEPLOY_CONTAINERS_REMOTE_STACK_NAME}' -f '${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE}' pull; \
    docker compose --env-file '${DEPLOY_CONTAINERS_REMOTE_ENV_FILE}' -p '${DEPLOY_CONTAINERS_REMOTE_STACK_NAME}' -f '${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE}' up ${DEPLOY_CONTAINERS_UP_FLAGS}"
else
  deploy_ssh_tty "set -euo pipefail; \
    cd '${DEPLOY_CONTAINERS_REMOTE_ROOT}'; \
    docker compose --env-file '${DEPLOY_CONTAINERS_REMOTE_ENV_FILE}' -p '${DEPLOY_CONTAINERS_REMOTE_STACK_NAME}' -f '${DEPLOY_CONTAINERS_REMOTE_COMPOSE_FILE}' up ${DEPLOY_CONTAINERS_UP_FLAGS}"
fi

echo "==> Container deployment complete"
echo "    Remote root: ${DEPLOY_CONTAINERS_REMOTE_ROOT}"
echo "    Compose file: ${REMOTE_COMPOSE_PATH}"
echo "    Env file (remote): ${REMOTE_ENV_PATH}"
