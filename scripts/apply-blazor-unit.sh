#!/usr/bin/env bash
# Stage and install a Blazor systemd unit from this repo onto the server.
#
# Deploy scripts only restart units; nothing in this repo installs them. That is
# deliberate — a unit change can stop a service from starting at all — so this
# is the explicit, separate step for when infra/*.service has changed.
#
# The file is copied with a non-sudo ssh (safe to pipe), then installed with an
# interactive sudo that keeps its own terminal. Never pipe the sudo half: that
# is how the password ends up echoed. See scripts/lib/remote-exec.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

# shellcheck source=deploy-config.sh
source "${SCRIPT_DIR}/deploy-config.sh"

UNIT="${1:-happygymstats-blazor}"
case "${UNIT}" in
  happygymstats-blazor|happygymstats-blazor-dev) ;;
  *)
    echo "usage: $0 [happygymstats-blazor|happygymstats-blazor-dev]" >&2
    exit 2
    ;;
esac

SOURCE="${ROOT_DIR}/infra/${UNIT}.service"
[[ -f "${SOURCE}" ]] || { echo "missing ${SOURCE}" >&2; exit 1; }

STAGING="/tmp/${UNIT}.service.${DEPLOY_SSH_USER}.staging"

echo "==> Staging ${UNIT}.service (no sudo, so this may be piped)"
deploy_ssh_pipe "set -euo pipefail; cat > '${STAGING}'" < "${SOURCE}"

echo "==> Installing and restarting ${UNIT}"
deploy_ssh_tty "set -euo pipefail; \
  ${DEPLOY_SUDO_CMD} install -m 0644 '${STAGING}' '/etc/systemd/system/${UNIT}.service'; \
  rm -f '${STAGING}'; \
  ${DEPLOY_SUDO_CMD} systemctl daemon-reload; \
  ${DEPLOY_SUDO_CMD} systemctl restart '${UNIT}'; \
  ${DEPLOY_SUDO_CMD} systemctl --no-pager --full status '${UNIT}' | head -n 12"

echo "==> Post-conditions"
deploy_ssh_tty "set -euo pipefail; \
  echo -n '    key ring: '; ls -ld '/var/lib/${UNIT}' 2>/dev/null || echo 'MISSING — StateDirectory not applied'; \
  echo -n '    secret warning: '; \
  if ${DEPLOY_SUDO_CMD} journalctl -u '${UNIT}' -b | grep -q RequireClientSecret; then \
    echo 'PRESENT — the client secret is not reaching the app'; \
  else \
    echo 'none'; \
  fi"
