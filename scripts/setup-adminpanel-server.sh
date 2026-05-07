#!/usr/bin/env bash
# setup-adminpanel-server.sh — Safe remote bootstrap for AdminPanel nginx route.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly ADMIN_NGINX_SOURCE="${ROOT_DIR}/infra/nginx-adminpanel.conf"

usage() {
  cat <<'EOF'
Usage: bash scripts/setup-adminpanel-server.sh [--execute] [--confirm-remote-setup]

By default this script performs local/static checks only and prints the remote setup command.
It mutates remote nginx configuration only when both flags are present:
  --execute
  --confirm-remote-setup

Environment overrides:
  DEPLOY_SSH_HOST                (default: ssh.geromet.com)
  DEPLOY_SSH_USER                (default: anon)
  DEPLOY_SSH_KEY                 (default: ~/.ssh/id_token2_bio3_hetzner)
  DEPLOY_PROXY_COMMAND           (default: cloudflared access ssh --hostname ssh.geromet.com)
  DEPLOY_USE_SUDO                (default: 1)
  DEPLOY_SUDO_NON_INTERACTIVE    (default: 0)
  DEPLOY_INSTALL_ADMIN_NGINX     (default: 0; set to 1 once DNS/TLS is ready)
  DEPLOY_ADMIN_NGINX_NAME        (default: nginx-adminpanel.conf)
  DEPLOY_ADMIN_NGINX_TARGET_DIR  (default: /etc/nginx/sites-available)
  DEPLOY_ADMIN_NGINX_LINK_DIR    (default: /etc/nginx/sites-enabled)
  DEPLOY_ADMIN_NGINX_CONF_D_DIR  (default: /etc/nginx/conf.d)
  DEPLOY_ADMIN_NGINX_USE_CONF_D  (default: 0; set to 1 for conf.d-based installs)
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_USE_SUDO:=1}"
: "${DEPLOY_SUDO_NON_INTERACTIVE:=0}"
: "${DEPLOY_INSTALL_ADMIN_NGINX:=0}"
: "${DEPLOY_ADMIN_NGINX_NAME:=nginx-adminpanel.conf}"
: "${DEPLOY_ADMIN_NGINX_TARGET_DIR:=/etc/nginx/sites-available}"
: "${DEPLOY_ADMIN_NGINX_LINK_DIR:=/etc/nginx/sites-enabled}"
: "${DEPLOY_ADMIN_NGINX_CONF_D_DIR:=/etc/nginx/conf.d}"
: "${DEPLOY_ADMIN_NGINX_USE_CONF_D:=0}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_cmd_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_cmd_pipe() { ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

if [[ ! -f "${ADMIN_NGINX_SOURCE}" ]]; then
  echo "ERROR: Missing nginx config source: ${ADMIN_NGINX_SOURCE}" >&2
  exit 1
fi

RUN_REMOTE_SETUP=0
CONFIRM_REMOTE_SETUP=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --execute)
      RUN_REMOTE_SETUP=1
      ;;
    --confirm-remote-setup)
      CONFIRM_REMOTE_SETUP=1
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
  shift
done

if [[ "${DEPLOY_USE_SUDO}" == "1" ]]; then
  [[ "${DEPLOY_SUDO_NON_INTERACTIVE}" == "1" ]] && SUDO_CMD="sudo -n" || SUDO_CMD="sudo"
else
  SUDO_CMD=""
fi

readonly REMOTE_STAGING_FILE="/tmp/${DEPLOY_ADMIN_NGINX_NAME}.${DEPLOY_SSH_USER}.staging"
readonly REMOTE_TARGET_FILE="${DEPLOY_ADMIN_NGINX_TARGET_DIR}/${DEPLOY_ADMIN_NGINX_NAME}"
readonly REMOTE_LINK_FILE="${DEPLOY_ADMIN_NGINX_LINK_DIR}/${DEPLOY_ADMIN_NGINX_NAME}"
readonly REMOTE_CONF_D_FILE="${DEPLOY_ADMIN_NGINX_CONF_D_DIR}/${DEPLOY_ADMIN_NGINX_NAME}"

cat <<EOF
==> Local preflight complete
    source: ${ADMIN_NGINX_SOURCE}
    remote setup host: ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}
    DEPLOY_INSTALL_ADMIN_NGINX=${DEPLOY_INSTALL_ADMIN_NGINX}
    mode: $( [[ "${DEPLOY_ADMIN_NGINX_USE_CONF_D}" == "1" ]] && echo "conf.d" || echo "sites-available/sites-enabled" )
EOF

if [[ "${DEPLOY_INSTALL_ADMIN_NGINX}" != "1" ]]; then
  echo "==> Skipping remote nginx install because DEPLOY_INSTALL_ADMIN_NGINX=${DEPLOY_INSTALL_ADMIN_NGINX}."
  echo "    Set DEPLOY_INSTALL_ADMIN_NGINX=1 only after DNS/TLS is ready for admin host."
  exit 0
fi

if [[ "${RUN_REMOTE_SETUP}" != "1" || "${CONFIRM_REMOTE_SETUP}" != "1" ]]; then
  echo "==> Remote setup is gated by explicit user confirmation."
  echo "    To mutate remote nginx, re-run with: --execute --confirm-remote-setup"
  echo "    Local/static verification is allowed without those flags."
  exit 0
fi

echo "==> Staging nginx-adminpanel config to remote temp path"
cat "${ADMIN_NGINX_SOURCE}" | ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_STAGING_FILE}'"

echo "==> Installing nginx-adminpanel config (idempotent)"
if [[ "${DEPLOY_ADMIN_NGINX_USE_CONF_D}" == "1" ]]; then
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_ADMIN_NGINX_CONF_D_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_STAGING_FILE}' '${REMOTE_CONF_D_FILE}'; \
    rm -f '${REMOTE_STAGING_FILE}'"
else
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_ADMIN_NGINX_TARGET_DIR}' '${DEPLOY_ADMIN_NGINX_LINK_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_STAGING_FILE}' '${REMOTE_TARGET_FILE}'; \
    ${SUDO_CMD} ln -sfn '${REMOTE_TARGET_FILE}' '${REMOTE_LINK_FILE}'; \
    rm -f '${REMOTE_STAGING_FILE}'"
fi

echo "==> Running nginx -t"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} nginx -t"

echo "==> Reload nginx"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} systemctl reload nginx"

echo "==> AdminPanel nginx route setup complete"
