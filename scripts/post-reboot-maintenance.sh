#!/usr/bin/env bash
# post-reboot-maintenance.sh — The maintenance pass after the kernel reboot.
#
# SCRIPT_CATEGORY=manual-maintenance
# SCRIPT_MUTATES_SERVER_STATE=conditional
# SCRIPT_AUTOMATION_SAFE_DEFAULT=1
#
# Survey-and-plan by default. Mutating requires --execute --confirm-maintenance
# AND DEPLOY_RUN_MAINTENANCE=1.
#
# Steps, each independently selectable:
#
#   nginx-catchall  Replace the stock nginx 'default' site with a minimal
#                   catch-all that closes the connection (444).
#
#                   Note: simply DELETING the default site does not do what it
#                   sounds like. Without a default_server, nginx serves the
#                   FIRST matching block to any unmatched hostname — so removing
#                   'default' would make torndev.geromet.com (and any hostname
#                   pointed at this IP) reach a real application instead of a
#                   stub. That is worse, not better. The catch-all is what
#                   actually closes the hole, and it is far lighter than the
#                   stock site it replaces.
#
#   swap            Create a swap file. The host has 3.7G RAM and NO swap, which
#                   means memory pressure produces OOM kills rather than
#                   slowdown. Two more .NET services are planned.
#
#   keycloak-heap   Cap the Keycloak JVM heap. It is the largest single consumer
#                   at ~650MB because the JVM sizes its heap as a fraction of
#                   total RAM and nothing told it otherwise.
#
# Memory is measured before and after so the effect is observed rather than
# assumed.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: bash scripts/post-reboot-maintenance.sh [--execute --confirm-maintenance] [options]

SCRIPT_CATEGORY=manual-maintenance
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Options:
  --steps LIST        Comma-separated: nginx-catchall,swap,keycloak-heap
                      (default: swap,keycloak-heap)
                      nginx-catchall is OFF by default: the stock 'default' site
                      is being kept deliberately. Opt in only if that changes.
  --swap-size SIZE    Swap file size, e.g. 2G or 4G (default: 2G)
  --swappiness N      vm.swappiness (default: 10 — swap as a safety net for a
                      server, not as routine paging)
  --keycloak-heap MB  Max JVM heap in MB for Keycloak (default: 384)
  --edit-compose      Allow editing the docker compose file for the Keycloak
                      heap change, after backing it up and showing a diff.
                      Without this the change is printed for you to apply.
  --status            Report current state of all three items, then exit
  --execute --confirm-maintenance   Apply (also needs DEPLOY_RUN_MAINTENANCE=1)

ORDER
  Run this AFTER the reboot that activates the pending kernel. Container
  upgrades are a separate script:
      bash scripts/upgrade-containers.sh

SAFETY
  - nginx changes are validated with `nginx -t` BEFORE reload, and the previous
    config is backed up. A failed test aborts without reloading.
  - The swap file is created with the correct 0600 mode and only added to
    /etc/fstab once; re-running is a no-op.
  - Compose files are backed up before any edit, and the diff is shown.
EOF
}

STEPS="swap,keycloak-heap"
SWAP_SIZE="2G"
SWAPPINESS="10"
KC_HEAP_MB="384"
EDIT_COMPOSE=0
EXECUTE=0
CONFIRM=0
SHOW_STATUS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --steps) STEPS="${2:-all}"; shift 2 ;;
    --swap-size) SWAP_SIZE="${2:-2G}"; shift 2 ;;
    --swappiness) SWAPPINESS="${2:-10}"; shift 2 ;;
    --keycloak-heap) KC_HEAP_MB="${2:-384}"; shift 2 ;;
    --edit-compose) EDIT_COMPOSE=1; shift ;;
    --status) SHOW_STATUS=1; shift ;;
    --execute) EXECUTE=1; shift ;;
    --confirm-maintenance) CONFIRM=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

[[ "${SWAP_SIZE}" =~ ^[0-9]+[MG]$ ]] || { echo "Invalid --swap-size '${SWAP_SIZE}' (e.g. 2G, 512M)" >&2; exit 1; }
[[ "${SWAPPINESS}" =~ ^[0-9]+$ ]] && (( SWAPPINESS <= 100 )) || { echo "Invalid --swappiness '${SWAPPINESS}' (0-100)" >&2; exit 1; }
[[ "${KC_HEAP_MB}" =~ ^[0-9]+$ ]] && (( KC_HEAP_MB >= 128 )) || { echo "Invalid --keycloak-heap '${KC_HEAP_MB}' (MB, min 128)" >&2; exit 1; }

want_step() {
  [[ "${STEPS}" == "all" ]] && return 0
  [[ ",${STEPS}," == *",$1,"* ]]
}
for s in ${STEPS//,/ }; do
  case "$s" in all|nginx-catchall|swap|keycloak-heap) ;; *) echo "Unknown step: $s" >&2; exit 1 ;; esac
done

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi
: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_RUN_MAINTENANCE:=0}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_tty() { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

# ── Survey / status ───────────────────────────────────────────────────────
echo "==> Surveying ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST} (read-only)"
echo

SURVEY_TMP="$(mktemp)"
trap 'rm -f "${SURVEY_TMP}"' EXIT

ssh_tty "bash -s" <<'REMOTE' 2>&1 | tee "${SURVEY_TMP}" | sed 's/^/    /' || true
set -uo pipefail
SUDO=""; [ "$(id -u)" != "0" ] && { sudo -n true 2>/dev/null && SUDO="sudo -n" || SUDO="sudo"; }

echo "--- kernel ---"
echo "  running:   $(uname -r)"
echo "  newest:    $(ls -1 /boot/vmlinuz-* 2>/dev/null | sed 's|.*/vmlinuz-||' | sort -V | tail -1)"
[ -f /var/run/reboot-required ] && echo "  REBOOT STILL PENDING" || echo "  no reboot pending"

echo "--- memory ---"
free -m | sed 's/^/  /'
echo "  top consumers:"
ps -eo rss,comm --sort=-rss 2>/dev/null | head -8 | awk 'NR>1{printf "    %8s KB  %s\n", $1, $2}'

echo "--- swap ---"
if swapon --show=NAME --noheadings 2>/dev/null | grep -q .; then
  echo "  ACTIVE:"; swapon --show 2>/dev/null | sed 's/^/    /'
else
  echo "  none active"
fi
grep -q '^/swapfile' /etc/fstab 2>/dev/null && echo "  /swapfile present in /etc/fstab" || echo "  /swapfile not in /etc/fstab"
echo "  vm.swappiness=$(cat /proc/sys/vm/swappiness 2>/dev/null)"
echo "  disk free on /: $(df -BG --output=avail / 2>/dev/null | tail -1 | tr -d ' ')"

echo "--- nginx default site ---"
if [ -e /etc/nginx/sites-enabled/default ]; then
  echo "  stock 'default' site is ENABLED"
  ls -l /etc/nginx/sites-enabled/default | sed 's/^/    /'
else
  echo "  stock 'default' site not enabled"
fi
echo "  default_server declarations:"
${SUDO} grep -rn 'default_server' /etc/nginx/sites-enabled/ /etc/nginx/conf.d/ 2>/dev/null | sed 's/^/    /' || echo "    (none)"

echo "--- keycloak ---"
KC="$(${SUDO} docker ps --format '{{.Names}}' 2>/dev/null | grep -i keycloak | head -1)"
if [ -n "${KC}" ]; then
  echo "  container: ${KC}"
  echo "  memory:    $(${SUDO} docker stats --no-stream --format '{{.MemUsage}}' "${KC}" 2>/dev/null)"
  echo "  heap env:  $(${SUDO} docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "${KC}" 2>/dev/null | grep -iE 'JAVA_OPTS|HEAP|Xmx' || echo '(no heap setting — JVM sizes from total RAM)')"
  echo "  compose:   $(${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "${KC}" 2>/dev/null)"
  echo "  service:   $(${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "${KC}" 2>/dev/null)"
else
  echo "  no keycloak container found"
fi
echo "SURVEY_OK=1"
REMOTE

sed -i 's/\r$//' "${SURVEY_TMP}" 2>/dev/null || true
sed -i '/^\[sudo\] password for /d' "${SURVEY_TMP}" 2>/dev/null || true
echo

if ! grep -q '^SURVEY_OK=1' "${SURVEY_TMP}"; then
  echo "MAINTENANCE_FAIL category=survey_unreachable" >&2
  echo "    Nothing is known about the host. That is not the same as 'nothing to do'." >&2
  echo "    Most likely: cloudflared access login https://ssh.geromet.com" >&2
  exit 1
fi

(( SHOW_STATUS )) && exit 0

if grep -q 'REBOOT STILL PENDING' "${SURVEY_TMP}"; then
  echo "!! A reboot is still pending. This script is meant to run AFTER the reboot"
  echo "!! that activates the new kernel. Continuing is allowed, but the kernel"
  echo "!! finding stays open until the machine restarts."
  echo
fi

KC_COMPOSE_FILE="$(grep -E '^\s*compose:' "${SURVEY_TMP}" | head -1 | sed 's/.*compose:[[:space:]]*//')"
KC_SERVICE="$(grep -E '^\s*service:' "${SURVEY_TMP}" | head -1 | sed 's/.*service:[[:space:]]*//')"

# ── Plan ──────────────────────────────────────────────────────────────────
echo "==> Plan"
want_step nginx-catchall && {
  echo "    [nginx-catchall] replace stock 'default' site with a 444 catch-all"
  echo "                     validated with nginx -t before reload; old config backed up"
}
want_step swap && {
  echo "    [swap]           create /swapfile ${SWAP_SIZE}, vm.swappiness=${SWAPPINESS}, persist in fstab"
}
want_step keycloak-heap && {
  echo "    [keycloak-heap]  cap Keycloak JVM heap at ${KC_HEAP_MB}MB"
  echo "                     compose file: ${KC_COMPOSE_FILE:-unknown}"
  echo "                     $( ((EDIT_COMPOSE)) && echo 'will edit the compose file (backed up, diff shown)' || echo 'will PRINT the required edit; pass --edit-compose to apply it' )"
}
echo

if [[ "${DEPLOY_RUN_MAINTENANCE}" != "1" ]]; then
  echo "==> DRY RUN — nothing was changed."
  echo "    Set DEPLOY_RUN_MAINTENANCE=1 to enable mutation."
  exit 0
fi
if (( EXECUTE != 1 || CONFIRM != 1 )); then
  echo "==> DRY RUN — nothing was changed."
  echo "    To apply, re-run with: --execute --confirm-maintenance"
  exit 0
fi

# ── Execute ───────────────────────────────────────────────────────────────
echo "==> Applying"

DO_NGINX=0; want_step nginx-catchall && DO_NGINX=1
DO_SWAP=0;  want_step swap && DO_SWAP=1
DO_HEAP=0;  want_step keycloak-heap && DO_HEAP=1

ssh_tty "bash -s" <<REMOTE
set -uo pipefail
SUDO=""; [ "\$(id -u)" != "0" ] && { sudo -n true 2>/dev/null && SUDO="sudo -n" || SUDO="sudo"; }

DO_NGINX=${DO_NGINX}
DO_SWAP=${DO_SWAP}
DO_HEAP=${DO_HEAP}
SWAP_SIZE='${SWAP_SIZE}'
SWAPPINESS='${SWAPPINESS}'
KC_HEAP_MB='${KC_HEAP_MB}'
EDIT_COMPOSE=${EDIT_COMPOSE}
STAMP="\$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_DIR=/var/backups/happygymstats-maintenance
\${SUDO} mkdir -p "\${BACKUP_DIR}"

echo "=== memory BEFORE ==="
free -m | sed 's/^/  /'
echo

# ---------------------------------------------------------------- nginx ---
if [ "\${DO_NGINX}" = "1" ]; then
  echo "--> [nginx-catchall]"
  \${SUDO} tar -czf "\${BACKUP_DIR}/nginx-\${STAMP}.tar.gz" -C /etc nginx 2>/dev/null \
    && echo "    backed up /etc/nginx -> \${BACKUP_DIR}/nginx-\${STAMP}.tar.gz"

  # A catch-all is what actually closes the hole. Deleting 'default' alone would
  # hand unmatched hostnames to the first real server block instead.
  \${SUDO} tee /etc/nginx/sites-available/000-catchall.conf >/dev/null <<'CONF'
# Managed by scripts/post-reboot-maintenance.sh
#
# Answers any hostname that no other server block claims, and closes the
# connection without a response (444). Replaces the stock 'default' site, which
# served a real page and carried far more config than this.
#
# Without a default_server, nginx hands unmatched hostnames to the FIRST
# matching block — which would be a real application. That is the hole this
# closes.
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;
    return 444;
}

server {
    listen 443 ssl default_server;
    listen [::]:443 ssl default_server;
    server_name _;

    ssl_certificate     /etc/ssl/cloudflare/origin.pem;
    ssl_certificate_key /etc/ssl/cloudflare/origin.key;

    return 444;
}
CONF

  \${SUDO} ln -sfn /etc/nginx/sites-available/000-catchall.conf /etc/nginx/sites-enabled/000-catchall.conf
  \${SUDO} rm -f /etc/nginx/sites-enabled/default

  echo "    testing config before reload"
  if \${SUDO} nginx -t 2>&1 | sed 's/^/      /'; then
    \${SUDO} systemctl reload nginx && echo "    nginx reloaded"
  else
    echo "    !! nginx -t FAILED — restoring previous config, not reloading" >&2
    \${SUDO} rm -f /etc/nginx/sites-enabled/000-catchall.conf
    \${SUDO} tar -xzf "\${BACKUP_DIR}/nginx-\${STAMP}.tar.gz" -C /etc
    \${SUDO} nginx -t >/dev/null 2>&1 && echo "    restored config validates" >&2
    exit 1
  fi
  echo "    enabled sites now:"
  ls -1 /etc/nginx/sites-enabled/ | sed 's/^/      /'
  echo
fi

# ----------------------------------------------------------------- swap ---
if [ "\${DO_SWAP}" = "1" ]; then
  echo "--> [swap]"
  if swapon --show=NAME --noheadings 2>/dev/null | grep -q .; then
    echo "    swap already active — leaving it alone:"
    swapon --show 2>/dev/null | sed 's/^/      /'
  else
    AVAIL_G="\$(df -BG --output=avail / | tail -1 | tr -dc '0-9')"
    WANT_G="\$(printf '%s' "\${SWAP_SIZE}" | tr -dc '0-9')"
    case "\${SWAP_SIZE}" in *M) WANT_G=1 ;; esac
    if [ "\${AVAIL_G}" -lt \$(( WANT_G + 5 )) ]; then
      echo "    !! only \${AVAIL_G}G free; refusing to create a \${SWAP_SIZE} swap file" >&2
    else
      echo "    creating /swapfile (\${SWAP_SIZE})"
      if ! \${SUDO} fallocate -l "\${SWAP_SIZE}" /swapfile 2>/dev/null; then
        COUNT="\$(( WANT_G * 1024 ))"
        \${SUDO} dd if=/dev/zero of=/swapfile bs=1M count="\${COUNT}" status=none
      fi
      \${SUDO} chmod 600 /swapfile
      \${SUDO} mkswap /swapfile >/dev/null
      \${SUDO} swapon /swapfile
      if ! grep -q '^/swapfile' /etc/fstab; then
        echo '/swapfile none swap sw 0 0' | \${SUDO} tee -a /etc/fstab >/dev/null
        echo "    added to /etc/fstab"
      else
        echo "    already in /etc/fstab"
      fi
      echo "    active:"; swapon --show | sed 's/^/      /'
    fi
  fi
  echo "    setting vm.swappiness=\${SWAPPINESS}"
  \${SUDO} tee /etc/sysctl.d/60-happygymstats-swappiness.conf >/dev/null <<SYSCTL
# Managed by scripts/post-reboot-maintenance.sh
# Low value: swap is a safety net against OOM kills on this 3.7G host, not a
# routine paging tier.
vm.swappiness = \${SWAPPINESS}
SYSCTL
  \${SUDO} sysctl -q -w vm.swappiness="\${SWAPPINESS}"
  echo "    vm.swappiness=\$(cat /proc/sys/vm/swappiness)"
  echo
fi

# --------------------------------------------------------- keycloak heap ---
if [ "\${DO_HEAP}" = "1" ]; then
  echo "--> [keycloak-heap]"
  KC="\$(\${SUDO} docker ps --format '{{.Names}}' 2>/dev/null | grep -i keycloak | head -1)"
  if [ -z "\${KC}" ]; then
    echo "    no keycloak container; skipping"
  else
    CF="\$(\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "\${KC}" 2>/dev/null)"
    SVC="\$(\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "\${KC}" 2>/dev/null)"
    echo "    container=\${KC} service=\${SVC}"
    echo "    compose file=\${CF}"
    echo "    current usage: \$(\${SUDO} docker stats --no-stream --format '{{.MemUsage}}' "\${KC}" 2>/dev/null)"
    echo
    echo "    Required change — add to the '\${SVC}' service environment:"
    echo "        JAVA_OPTS_KC_HEAP: \"-Xms64m -Xmx\${KC_HEAP_MB}m\""
    echo
    if [ "\${EDIT_COMPOSE}" = "1" ] && [ -n "\${CF}" ] && [ -f "\${CF}" ]; then
      \${SUDO} cp -a "\${CF}" "\${BACKUP_DIR}/\$(basename "\${CF}").\${STAMP}.bak"
      echo "    backed up -> \${BACKUP_DIR}/\$(basename "\${CF}").\${STAMP}.bak"
      if grep -q 'JAVA_OPTS_KC_HEAP' "\${CF}"; then
        echo "    JAVA_OPTS_KC_HEAP already present — updating value"
        \${SUDO} sed -i "s|JAVA_OPTS_KC_HEAP:.*|JAVA_OPTS_KC_HEAP: \\"-Xms64m -Xmx\${KC_HEAP_MB}m\\"|" "\${CF}"
      else
        echo "    !! JAVA_OPTS_KC_HEAP not present. Inserting into a compose file"
        echo "    !! reliably needs YAML awareness that sed does not have, so this"
        echo "    !! is NOT edited automatically. Add the line shown above under"
        echo "    !! the '\${SVC}' service's environment: block, then run:"
        echo "    !!   \${SUDO} docker compose -f '\${CF}' up -d \${SVC}"
      fi
      echo "    diff:"
      \${SUDO} diff -u "\${BACKUP_DIR}/\$(basename "\${CF}").\${STAMP}.bak" "\${CF}" | sed 's/^/      /' || true
    else
      echo "    (not editing the compose file; pass --edit-compose to allow it)"
    fi
    echo
    echo "    Apply with:  \${SUDO} docker compose -f '\${CF}' up -d \${SVC}"
    echo "    Keycloak restarts and signs everyone out; expect ~30s."
  fi
  echo
fi

echo "=== memory AFTER ==="
free -m | sed 's/^/  /'
echo
echo "(Keycloak heap changes only take effect once the container is recreated.)"
REMOTE

cat <<EOF

==> Maintenance pass complete

  Re-check:  bash scripts/post-reboot-maintenance.sh --status
  Audit:     bash scripts/recon-fetch.sh security --sudo
  Ports:     bash scripts/recon-fetch.sh ports --sudo

  Still separate:
    bash scripts/upgrade-containers.sh          # Keycloak 26.7.2 / Postgres 16.15
    bash scripts/remove-teamspeak.sh            # frees RAM and closes 30033
EOF
