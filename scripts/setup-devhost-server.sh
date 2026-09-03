#!/usr/bin/env bash
# setup-devhost-server.sh — Safe remote bootstrap for the torndev.geromet.com dev host.
#
# Installs the nginx server block, the two dev systemd units, the release roots
# and an /etc/happygymstats/api-dev.env skeleton. Deploying code onto the host
# afterwards is scripts/deploy-dev.sh, not this script.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DEV_NGINX_SOURCE="${ROOT_DIR}/infra/nginx-torndev.conf"
readonly DEV_API_UNIT_SOURCE="${ROOT_DIR}/infra/happygymstats-api-dev.service"
readonly DEV_BLAZOR_UNIT_SOURCE="${ROOT_DIR}/infra/happygymstats-blazor-dev.service"

usage() {
  cat <<'EOF'
Usage: bash scripts/setup-devhost-server.sh [--execute] [--confirm-remote-setup]

SCRIPT_CATEGORY=manual-bootstrap
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

By default this performs local/static checks only and prints what it would do.
It mutates the remote host only when both flags are present AND
DEPLOY_INSTALL_DEV_HOST=1:
  --execute
  --confirm-remote-setup

Operator prerequisites (this script cannot do these for you):
  1. Cloudflare DNS A record: torndev.geromet.com -> the server's origin IP.
     Single label on purpose; the origin cert covers *.geromet.com but not
     *.*.geromet.com, so dev.torn.geromet.com would need a paid tier.
  2. Keycloak client 'happygymstats-web-dev' in realm 'torn':
       - Valid redirect URI: https://torndev.geromet.com/signin-oidc
       - Valid post logout redirect URI: https://torndev.geromet.com/signout-callback-oidc
       - Web origin: https://torndev.geromet.com
     A separate client (not a second redirect URI on happygymstats-web) is what
     keeps a production session from being reused against dev.
  3. A Postgres database and role for dev, separate from production.
     The API migrates at startup, so a shared database means a dev build can
     alter the production schema.
  4. Fill in /etc/happygymstats/api-dev.env after this script seeds it. The
     skeleton ships placeholder values that make the service fail fast rather
     than silently start against something real.

Environment overrides:
  DEPLOY_SSH_HOST                 (default: ssh.geromet.com)
  DEPLOY_SSH_USER                 (default: anon)
  DEPLOY_SSH_KEY                  (default: ~/.ssh/id_token2_bio3_hetzner)
  DEPLOY_PROXY_COMMAND            (default: cloudflared access ssh --hostname ssh.geromet.com)
  DEPLOY_USE_SUDO                 (default: 1)
  DEPLOY_SUDO_NON_INTERACTIVE     (default: 0)
  DEPLOY_INSTALL_DEV_HOST         (default: 0; set to 1 once DNS/TLS is ready)
  DEPLOY_DEV_NGINX_NAME           (default: nginx-torndev.conf)
  DEPLOY_DEV_NGINX_TARGET_DIR     (default: /etc/nginx/sites-available)
  DEPLOY_DEV_NGINX_LINK_DIR       (default: /etc/nginx/sites-enabled)
  DEPLOY_DEV_NGINX_CONF_D_DIR     (default: /etc/nginx/conf.d)
  DEPLOY_DEV_NGINX_USE_CONF_D     (default: 0; set to 1 for conf.d-based installs)
  DEPLOY_DEV_API_ROOT             (default: /var/www/happygymstats-dev)
  DEPLOY_DEV_BLAZOR_ROOT          (default: /var/www/happygymstats-blazor-dev)
  DEPLOY_DEV_ENV_FILE             (default: /etc/happygymstats/api-dev.env)
  DEPLOY_DEV_BLAZOR_ENV_FILE      (default: /etc/happygymstats/blazor-dev.env)
  DEPLOY_DEV_OWNER                (default: www-data)
  DEPLOY_DEV_GROUP                (default: www-data)
  DEPLOY_DEV_ENABLE_SERVICES      (default: 1; systemctl enable the new units)
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
: "${DEPLOY_INSTALL_DEV_HOST:=0}"
: "${DEPLOY_DEV_NGINX_NAME:=nginx-torndev.conf}"
: "${DEPLOY_DEV_NGINX_TARGET_DIR:=/etc/nginx/sites-available}"
: "${DEPLOY_DEV_NGINX_LINK_DIR:=/etc/nginx/sites-enabled}"
: "${DEPLOY_DEV_NGINX_CONF_D_DIR:=/etc/nginx/conf.d}"
: "${DEPLOY_DEV_NGINX_USE_CONF_D:=0}"
: "${DEPLOY_DEV_API_ROOT:=/var/www/happygymstats-dev}"
: "${DEPLOY_DEV_BLAZOR_ROOT:=/var/www/happygymstats-blazor-dev}"
: "${DEPLOY_DEV_ENV_FILE:=/etc/happygymstats/api-dev.env}"
: "${DEPLOY_DEV_BLAZOR_ENV_FILE:=/etc/happygymstats/blazor-dev.env}"
: "${DEPLOY_DEV_OWNER:=www-data}"
: "${DEPLOY_DEV_GROUP:=www-data}"
: "${DEPLOY_DEV_ENABLE_SERVICES:=1}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_cmd_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_cmd_pipe() { ssh -T "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

for source_file in "${DEV_NGINX_SOURCE}" "${DEV_API_UNIT_SOURCE}" "${DEV_BLAZOR_UNIT_SOURCE}"; do
  if [[ ! -f "${source_file}" ]]; then
    echo "ERROR: Missing source file: ${source_file}" >&2
    exit 1
  fi
done

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

readonly REMOTE_NGINX_STAGING="/tmp/${DEPLOY_DEV_NGINX_NAME}.${DEPLOY_SSH_USER}.staging"
readonly REMOTE_NGINX_TARGET="${DEPLOY_DEV_NGINX_TARGET_DIR}/${DEPLOY_DEV_NGINX_NAME}"
readonly REMOTE_NGINX_LINK="${DEPLOY_DEV_NGINX_LINK_DIR}/${DEPLOY_DEV_NGINX_NAME}"
readonly REMOTE_NGINX_CONF_D="${DEPLOY_DEV_NGINX_CONF_D_DIR}/${DEPLOY_DEV_NGINX_NAME}"
readonly REMOTE_API_UNIT_STAGING="/tmp/happygymstats-api-dev.service.${DEPLOY_SSH_USER}.staging"
readonly REMOTE_BLAZOR_UNIT_STAGING="/tmp/happygymstats-blazor-dev.service.${DEPLOY_SSH_USER}.staging"
readonly REMOTE_ENV_STAGING="/tmp/api-dev.env.${DEPLOY_SSH_USER}.staging"
readonly REMOTE_BLAZOR_ENV_STAGING="/tmp/blazor-dev.env.${DEPLOY_SSH_USER}.staging"

cat <<EOF
==> Local preflight complete
SCRIPT_CATEGORY=manual-bootstrap
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1
    nginx source:   ${DEV_NGINX_SOURCE}
    api unit:       ${DEV_API_UNIT_SOURCE}
    blazor unit:    ${DEV_BLAZOR_UNIT_SOURCE}
    remote host:    ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}
    api root:       ${DEPLOY_DEV_API_ROOT}
    blazor root:    ${DEPLOY_DEV_BLAZOR_ROOT}
    env file:       ${DEPLOY_DEV_ENV_FILE}
    blazor env:     ${DEPLOY_DEV_BLAZOR_ENV_FILE}
    DEPLOY_INSTALL_DEV_HOST=${DEPLOY_INSTALL_DEV_HOST}
    mode: $( [[ "${DEPLOY_DEV_NGINX_USE_CONF_D}" == "1" ]] && echo "conf.d" || echo "sites-available/sites-enabled" )
EOF

if [[ "${DEPLOY_INSTALL_DEV_HOST}" != "1" ]]; then
  echo "==> Skipping remote dev-host install because DEPLOY_INSTALL_DEV_HOST=${DEPLOY_INSTALL_DEV_HOST}."
  echo "    Set DEPLOY_INSTALL_DEV_HOST=1 only after the torndev.geromet.com A record resolves."
  exit 0
fi

if [[ "${RUN_REMOTE_SETUP}" != "1" || "${CONFIRM_REMOTE_SETUP}" != "1" ]]; then
  echo "==> Remote setup is gated by explicit user confirmation."
  echo "    To mutate the remote host, re-run with: --execute --confirm-remote-setup"
  echo "    Local/static verification is allowed without those flags."
  exit 0
fi

echo "==> Checking dev ports are free on the remote host"
if ssh_cmd_pipe "set -euo pipefail; command -v ss >/dev/null" >/dev/null 2>&1; then
  for port in 5147 5282; do
    if ssh_cmd_pipe "set -euo pipefail; ss -ltn 2>/dev/null | grep -q ':${port} '" >/dev/null 2>&1; then
      echo "DEVHOST_SETUP_FAIL category=port_in_use port=${port}" >&2
      echo "    Something already listens on ${port}. Pick different dev ports and update" >&2
      echo "    infra/happygymstats-*-dev.service and infra/nginx-torndev.conf together." >&2
      exit 1
    fi
  done
  echo "    ports 5147 and 5282 are free"
else
  echo "    (ss unavailable on remote; skipping port check)"
fi

echo "==> Staging nginx config and systemd units"
ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_NGINX_STAGING}'" < "${DEV_NGINX_SOURCE}"
ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_API_UNIT_STAGING}'" < "${DEV_API_UNIT_SOURCE}"
ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_BLAZOR_UNIT_STAGING}'" < "${DEV_BLAZOR_UNIT_SOURCE}"

echo "==> Creating dev release roots"
ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p '${DEPLOY_DEV_API_ROOT}/releases' '${DEPLOY_DEV_API_ROOT}/data/surfaces-cache' '${DEPLOY_DEV_BLAZOR_ROOT}/releases'; \
  ${SUDO_CMD} chown -R '${DEPLOY_DEV_OWNER}:${DEPLOY_DEV_GROUP}' '${DEPLOY_DEV_API_ROOT}' '${DEPLOY_DEV_BLAZOR_ROOT}'"

echo "==> Seeding ${DEPLOY_DEV_ENV_FILE} if absent (never overwrites an existing file)"
cat <<'ENVSKELETON' | ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_ENV_STAGING}'"
# HappyGymStats dev host (torndev.geromet.com) API runtime environment.
# Seeded by scripts/setup-devhost-server.sh. Edit before starting the service.
#
# The placeholders below are intentionally invalid so the service fails loudly
# rather than starting against something real.
#
# MUST be a database separate from production — the API migrates at startup.
ConnectionStrings__HappyGymStats=Host=127.0.0.1;Database=REPLACE_ME_happygymstats_dev;Username=REPLACE_ME;Password=REPLACE_ME
# MUST differ from the production key, or dev-minted tokens are valid in production.
ProvisionalToken__SigningKey=REPLACE_ME
HAPPYGYMSTATS_SURFACES_CACHE_DIR=/var/www/happygymstats-dev/data/surfaces-cache
# Restrict browser origins to the dev host. "*" restores allow-any.
Cors__AllowedOrigins=https://torndev.geromet.com
ENVSKELETON

ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p \"\$(dirname '${DEPLOY_DEV_ENV_FILE}')\"; \
  if [[ -f '${DEPLOY_DEV_ENV_FILE}' ]]; then \
    echo '    ${DEPLOY_DEV_ENV_FILE} already exists — left untouched'; \
  else \
    ${SUDO_CMD} install -m 0640 -o root -g '${DEPLOY_DEV_GROUP}' '${REMOTE_ENV_STAGING}' '${DEPLOY_DEV_ENV_FILE}'; \
    echo '    seeded ${DEPLOY_DEV_ENV_FILE} — EDIT IT BEFORE STARTING THE SERVICE'; \
  fi; \
  rm -f '${REMOTE_ENV_STAGING}'"

echo "==> Seeding ${DEPLOY_DEV_BLAZOR_ENV_FILE} if absent (never overwrites an existing file)"
cat <<'BLAZORENVSKELETON' | ssh_cmd_pipe "set -euo pipefail; cat > '${REMOTE_BLAZOR_ENV_STAGING}'"
# HappyGymStats dev host (torndev.geromet.com) Blazor runtime environment.
# Seeded by scripts/setup-devhost-server.sh. Edit before starting the service.
#
# Secret of the CONFIDENTIAL Keycloak client 'happygymstats-web-dev', copied
# from Clients -> happygymstats-web-dev -> Credentials. It MUST differ from
# production's: they are separate clients, and sharing the value would let a
# dev-host compromise authenticate as the production frontend.
#
# This lives in a 0640 file rather than the unit because unit files are 0644.
Keycloak__ClientSecret=REPLACE_ME
BLAZORENVSKELETON

ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} mkdir -p \"\$(dirname '${DEPLOY_DEV_BLAZOR_ENV_FILE}')\"; \
  if [[ -f '${DEPLOY_DEV_BLAZOR_ENV_FILE}' ]]; then \
    echo '    ${DEPLOY_DEV_BLAZOR_ENV_FILE} already exists — left untouched'; \
  else \
    ${SUDO_CMD} install -m 0640 -o root -g '${DEPLOY_DEV_GROUP}' '${REMOTE_BLAZOR_ENV_STAGING}' '${DEPLOY_DEV_BLAZOR_ENV_FILE}'; \
    echo '    seeded ${DEPLOY_DEV_BLAZOR_ENV_FILE} — EDIT IT BEFORE STARTING THE SERVICE'; \
  fi; \
  rm -f '${REMOTE_BLAZOR_ENV_STAGING}'"

echo "==> Installing systemd units"
ssh_cmd_tty "set -euo pipefail; \
  ${SUDO_CMD} install -m 0644 '${REMOTE_API_UNIT_STAGING}' /etc/systemd/system/happygymstats-api-dev.service; \
  ${SUDO_CMD} install -m 0644 '${REMOTE_BLAZOR_UNIT_STAGING}' /etc/systemd/system/happygymstats-blazor-dev.service; \
  rm -f '${REMOTE_API_UNIT_STAGING}' '${REMOTE_BLAZOR_UNIT_STAGING}'; \
  ${SUDO_CMD} systemctl daemon-reload"

if [[ "${DEPLOY_DEV_ENABLE_SERVICES}" == "1" ]]; then
  echo "==> Enabling dev units (not starting them — no code is deployed yet)"
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} systemctl enable happygymstats-api-dev.service happygymstats-blazor-dev.service"
else
  echo "==> Skipping systemctl enable (DEPLOY_DEV_ENABLE_SERVICES=${DEPLOY_DEV_ENABLE_SERVICES})"
fi

echo "==> Installing nginx server block (idempotent)"
if [[ "${DEPLOY_DEV_NGINX_USE_CONF_D}" == "1" ]]; then
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_DEV_NGINX_CONF_D_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_NGINX_STAGING}' '${REMOTE_NGINX_CONF_D}'; \
    rm -f '${REMOTE_NGINX_STAGING}'"
else
  ssh_cmd_tty "set -euo pipefail; \
    ${SUDO_CMD} mkdir -p '${DEPLOY_DEV_NGINX_TARGET_DIR}' '${DEPLOY_DEV_NGINX_LINK_DIR}'; \
    ${SUDO_CMD} install -m 0644 '${REMOTE_NGINX_STAGING}' '${REMOTE_NGINX_TARGET}'; \
    ${SUDO_CMD} ln -sfn '${REMOTE_NGINX_TARGET}' '${REMOTE_NGINX_LINK}'; \
    rm -f '${REMOTE_NGINX_STAGING}'"
fi

echo "==> Running nginx -t"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} nginx -t"

echo "==> Reload nginx"
ssh_cmd_tty "set -euo pipefail; ${SUDO_CMD} systemctl reload nginx"

cat <<EOF
==> Dev host setup complete

Remaining operator steps before the site works:
  1. Edit ${DEPLOY_DEV_ENV_FILE} and ${DEPLOY_DEV_BLAZOR_ENV_FILE}, replacing
     every REPLACE_ME value.
     Create the dev Postgres database/role first if you have not already.
  2. Create the Keycloak client 'happygymstats-web-dev' (see --help for the
     exact redirect URIs) and add your account to the /admins group.
  3. Deploy code:  bash scripts/deploy-dev.sh
  4. Verify:       bash scripts/verify/devhost-smoke.sh
EOF
