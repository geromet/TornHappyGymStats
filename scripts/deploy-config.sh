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

: "${DEPLOY_SSH_CONTROL_DIR:=${TMPDIR:-/tmp}/happygymstats-deploy-ssh}"
mkdir -p "${DEPLOY_SSH_CONTROL_DIR}"
readonly DEPLOY_SSH_CONTROL_PATH="${DEPLOY_SSH_CONTROL_DIR}/%r@%h:%p"

DEPLOY_SSH_OPTS=(
  -i "${DEPLOY_SSH_KEY}"
  -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}"
  -o "ControlMaster=auto"
  -o "ControlPath=${DEPLOY_SSH_CONTROL_PATH}"
  -o "ControlPersist=10m"
)

deploy_ssh_tty() {
  ssh -tt "${DEPLOY_SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}

deploy_ssh_pipe() {
  ssh -T "${DEPLOY_SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"
}

deploy_ssh_control_close() {
  ssh -O exit -o "ControlPath=${DEPLOY_SSH_CONTROL_PATH}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" >/dev/null 2>&1 || true
}
trap deploy_ssh_control_close EXIT

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

deploy_precheck_fail() {
  local category="$1"
  local detail="$2"
  echo "DEPLOY_PRECHECK_FAIL category=${category} detail=${detail}" >&2
  return 1
}

deploy_precheck_require_local_file() {
  local path="$1"
  local category="${2:-missing_local_file}"
  [[ -f "${path}" ]] || deploy_precheck_fail "${category}" "path=${path}"
}

deploy_precheck_require_local_dir() {
  local path="$1"
  local category="${2:-missing_local_dir}"
  [[ -d "${path}" ]] || deploy_precheck_fail "${category}" "path=${path}"
}

deploy_precheck_require_local_command() {
  local cmd="$1"
  local category="${2:-missing_local_command}"
  command -v "${cmd}" >/dev/null 2>&1 || deploy_precheck_fail "${category}" "command=${cmd}"
}

deploy_precheck_require_executable_file() {
  local path="$1"
  local category="${2:-missing_executable_file}"

  if [[ ! -f "${path}" ]]; then
    deploy_precheck_fail "${category}" "path=${path} reason=missing"
    return 1
  fi

  if [[ ! -x "${path}" ]]; then
    deploy_precheck_fail "${category}" "path=${path} reason=not_executable"
    return 1
  fi
}

deploy_precheck_remote_command() {
  local cmd="$1"
  local category="${2:-missing_remote_command}"

  # Probe with a sentinel rather than trusting ssh's exit status. Suppressing
  # output to keep the precheck quiet also suppresses ssh's own passphrase
  # prompt and error text, so an unreachable host or an unanswered key
  # passphrase used to be reported as "this command is not installed" — which
  # sends the operator to apt-get on a machine that already has it.
  local probe
  probe="$(deploy_ssh_tty "set -euo pipefail; if command -v '${cmd}' >/dev/null; then echo PROBE_FOUND; else echo PROBE_ABSENT; fi" 2>/dev/null | tr -d '\r')"

  if [[ "${probe}" == *PROBE_ABSENT* ]]; then
    deploy_precheck_fail "${category}" "command=${cmd}"
  elif [[ "${probe}" != *PROBE_FOUND* ]]; then
    deploy_precheck_fail "remote_probe_unreachable" \
      "command=${cmd} detail=ssh_produced_no_answer hint=ssh-add_your_key_or_run_cloudflared_access_login"
  fi
}

deploy_precheck_remote_service_exists() {
  local service="$1"
  local setup_hint="${2:-}"
  local service_unit="${service}"
  [[ "${service_unit}" == *.service ]] || service_unit="${service_unit}.service"

  if ! deploy_ssh_tty "set -euo pipefail; systemctl list-unit-files --type=service --all | awk '{print \$1}' | grep -Fx '${service_unit}' >/dev/null" >/dev/null 2>&1; then
    local detail="service=${service_unit}"
    [[ -n "${setup_hint}" ]] && detail="${detail} hint=${setup_hint}"
    deploy_precheck_fail "missing_remote_service" "${detail}"
  fi
}

deploy_precheck_remote_path_writable() {
  local path="$1"
  local category="${2:-missing_remote_write_privilege}"

  if ! deploy_ssh_tty "set -euo pipefail; if [[ -e '${path}' ]]; then test -w '${path}'; else test -w \"\$(dirname '${path}')\"; fi" >/dev/null 2>&1; then
    if [[ -z "${DEPLOY_SUDO_CMD}" ]]; then
      deploy_precheck_fail "${category}" "path=${path}"
    elif [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]]; then
      # Automation mode: passwordless sudo is expected, so verify it non-interactively
      # rather than hanging on a password prompt nothing will ever answer.
      deploy_ssh_tty "set -euo pipefail; sudo -n test -w '${path}' || sudo -n test -w \"\$(dirname '${path}')\"" >/dev/null 2>&1 || deploy_precheck_fail "${category}" "path=${path} hint=passwordless_sudo_required"
    else
      # Interactive mode: sudo intentionally requires a password on this host, and this is
      # just a precheck, not the mutating step. Don't block here on a prompt we'd have to
      # show twice — trust the real deploy actions later (mkdir/rsync/chown) to prompt
      # visibly and fail loudly if write access genuinely isn't there.
      echo "==> Skipping write precheck for ${path} (requires interactive sudo; will confirm during deploy)"
    fi
  fi
}

deploy_precheck_remote_root_ready() {
  local remote_root="$1"
  local category="${2:-missing_remote_write_privilege}"
  deploy_precheck_remote_path_writable "${remote_root}" "${category}"
}

deploy_precheck_remote_sudo_access() {
  if [[ "${DEPLOY_USE_SUDO}" != "1" ]]; then
    return 0
  fi

  if [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]]; then
    deploy_ssh_tty "set -euo pipefail; sudo -n true" >/dev/null 2>&1 || deploy_precheck_fail "missing_remote_sudo_privilege" "sudo_non_interactive=true"
  fi
}

: "${DEPLOY_RUN_SMOKE:=0}"
: "${DEPLOY_SMOKE_SCRIPT:=${DEPLOY_CONFIG_DIR}/verify/production-smoke.sh}"
: "${DEPLOY_SMOKE_MODE:=remote}"

deploy_print_post_deploy_smoke_next_step() {
  local smoke_env="SMOKE_MODE=${DEPLOY_SMOKE_MODE}"
  local run_cmd="${smoke_env} bash ${DEPLOY_SMOKE_SCRIPT}"

  echo "==> Post-deploy verification"
  echo "    Next step: ${run_cmd}"
  echo "    Set DEPLOY_RUN_SMOKE=1 to run smoke automatically from scripts/deploy.sh"
}

deploy_run_post_deploy_smoke_if_enabled() {
  deploy_print_post_deploy_smoke_next_step

  if [[ "${DEPLOY_RUN_SMOKE}" != "1" ]]; then
    return 0
  fi

  if [[ ! -f "${DEPLOY_SMOKE_SCRIPT}" ]]; then
    echo "DEPLOY_SMOKE_FAIL category=missing_smoke_script path=${DEPLOY_SMOKE_SCRIPT}" >&2
    return 1
  fi

  echo "==> Running post-deploy production smoke"
  if SMOKE_MODE="${DEPLOY_SMOKE_MODE}" bash "${DEPLOY_SMOKE_SCRIPT}"; then
    echo "DEPLOY_SMOKE_PASS script=${DEPLOY_SMOKE_SCRIPT} mode=${DEPLOY_SMOKE_MODE}"
    return 0
  fi

  echo "DEPLOY_SMOKE_FAIL category=smoke_failed script=${DEPLOY_SMOKE_SCRIPT} mode=${DEPLOY_SMOKE_MODE}" >&2
  return 1
}
