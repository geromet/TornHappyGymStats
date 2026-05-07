#!/usr/bin/env bash
# Shared deploy configuration + helpers for deployment scripts.
# shellcheck shell=bash

# This file is intended to be sourced.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  echo "This file must be sourced by a deploy script, not executed directly." >&2
  exit 1
fi

if [[ -n "${_DEPLOY_CONFIG_LOADED:-}" ]]; then
  return 0
fi
readonly _DEPLOY_CONFIG_LOADED=1

readonly DEPLOY_CONFIG_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly DEPLOY_CONFIG_ROOT_DIR="$(cd "${DEPLOY_CONFIG_DIR}/.." && pwd)"
readonly DEPLOY_ENV_FILE="${DEPLOY_CONFIG_ROOT_DIR}/.env.deploy"

if [[ -f "${DEPLOY_ENV_FILE}" ]]; then
  # shellcheck disable=SC1090
  source "${DEPLOY_ENV_FILE}"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=0}"

# Map smoke defaults to deploy defaults when not explicitly set.
: "${SMOKE_SSH_HOST:=${DEPLOY_SSH_HOST}}"
: "${SMOKE_SSH_USER:=${DEPLOY_SSH_USER}}"
: "${SMOKE_SSH_KEY:=${DEPLOY_SSH_KEY}}"
: "${SMOKE_PROXY_COMMAND:=${DEPLOY_PROXY_COMMAND}}"

deploy_compute_sudo_cmd() {
  if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
    if [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]]; then
      printf 'sudo -n'
    else
      printf 'sudo'
    fi
  else
    printf ''
  fi
}

DEPLOY_SUDO_CMD="$(deploy_compute_sudo_cmd)"

DEPLOY_SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")

deploy_ssh_tty() {
  ssh -tt "${DEPLOY_SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}

deploy_ssh_pipe() {
  ssh -T "${DEPLOY_SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}

deploy_print_common_connection_summary() {
  local key_state="unset"
  local proxy_state="unset"
  [[ -n "${DEPLOY_SSH_KEY:-}" ]] && key_state="set"
  [[ -n "${DEPLOY_PROXY_COMMAND:-}" ]] && proxy_state="set"

  cat <<EOF
Connection target:
  DEPLOY_SSH_HOST=${DEPLOY_SSH_HOST}
  DEPLOY_SSH_USER=${DEPLOY_SSH_USER}
  DEPLOY_SSH_KEY=<${key_state}>
  DEPLOY_PROXY_COMMAND=<${proxy_state}>
  DEPLOY_USE_SUDO=${DEPLOY_USE_SUDO}
  DEPLOY_SUDO_NON_INTERACTIVE=${DEPLOY_SUDO_NON_INTERACTIVE}
EOF
}
