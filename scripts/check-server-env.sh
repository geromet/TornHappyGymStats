#!/usr/bin/env bash
# check-server-env.sh — Are the server's env files actually usable by the services?
#
# SCRIPT_CATEGORY=recon
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Read-only. Encodes the two failures that cost the most time on 2026-09-03,
# both of which look identical from the outside (a service that will not start,
# or a sign-in that fails with an opaque provider error):
#
#   * an `install /dev/null` after editing truncated the file  (pitfalls #3)
#   * a key was misspelled — `Keycloack__ClientSecret`         (pitfalls #4)
#
# It never prints a secret. Only key names, value LENGTHS, ownership and mode.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=deploy-config.sh
source "${SCRIPT_DIR}/deploy-config.sh"
# shellcheck source=lib/remote-exec.sh
source "${SCRIPT_DIR}/lib/remote-exec.sh"

usage() {
  cat <<'EOF'
Usage: bash scripts/check-server-env.sh

Read-only check of every runtime env file on the server:

  /etc/happygymstats/api.env
  /etc/happygymstats/api-dev.env
  /etc/happygymstats/blazor.env
  /etc/happygymstats/blazor-dev.env

For each it reports mode and owner, whether the owning service account can read
it, any REPLACE_ME left in place, and — for the keys the code actually binds —
whether the key is present and how long its value is.

Secrets are never printed. Values appear only as character counts.
EOF
}

case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  "") ;;
  *) echo "Unknown option '$1'. Try --help." >&2; exit 2 ;;
esac

echo "==> Checking runtime env files on ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
echo "    Read-only. No value is printed, only its length."
echo

# NOTE: unquoted heredoc — every $ that must survive to the server is escaped.
# See scripts/verify/remote-heredoc-lint.sh and pitfalls #2.
remote_exec_script --indent <<REMOTE
set -uo pipefail
# SUDO is supplied by remote_exec_script's preamble. Do NOT redefine it.

check_key() {
  FILE="\$1"
  KEY="\$2"
  LINE="\$(\${SUDO} grep -m1 "^\${KEY}=" "\$FILE" 2>/dev/null || true)"
  if [ -z "\$LINE" ]; then
    # A near-miss is far more useful than "missing": it is almost always a typo,
    # and the binder ignores the key silently.
    NEAR="\$(\${SUDO} grep -iE "^[A-Za-z_]*\$(echo "\$KEY" | sed 's/^Keycloak__//;s/^ConnectionStrings__//')=" "\$FILE" 2>/dev/null | cut -d= -f1 || true)"
    if [ -n "\$NEAR" ]; then
      echo "    !! \${KEY} MISSING - but the file has: \${NEAR}  <-- looks like a typo"
    else
      echo "    !! \${KEY} MISSING"
    fi
    return
  fi
  VALUE="\${LINE#*=}"
  LEN=\${#VALUE}
  case "\$VALUE" in
    *REPLACE_ME*) echo "    !! \${KEY} still contains REPLACE_ME" ;;
    "")           echo "    !! \${KEY} present but EMPTY (was the file truncated after editing?)" ;;
    *)            echo "    ok \${KEY} set (\${LEN} chars)" ;;
  esac
}

check_file() {
  FILE="\$1"
  OWNER_ACCOUNT="\$2"
  shift 2

  echo "\${FILE}"
  if ! \${SUDO} test -f "\$FILE"; then
    echo "    -- absent (fine if that host is not installed)"
    echo
    return
  fi

  META="\$(\${SUDO} stat -c '%a %U:%G %s' "\$FILE" 2>/dev/null || echo '? ? ?')"
  MODE="\$(echo "\$META" | cut -d' ' -f1)"
  OWNER="\$(echo "\$META" | cut -d' ' -f2)"
  SIZE="\$(echo "\$META" | cut -d' ' -f3)"
  echo "    mode \${MODE}  owner \${OWNER}  size \${SIZE} bytes"

  if [ "\$SIZE" = "0" ]; then
    echo "    !! EMPTY. 'install /dev/null <file>' truncates - create first, edit second."
  fi

  case "\$MODE" in
    640|600|644) : ;;
    *) echo "    !! unexpected mode \${MODE}; expected 0640" ;;
  esac

  # The check that matters: can the service account actually read it? A 0640
  # file owned root:root is invisible to www-data and fails exactly like a
  # missing secret.
  if \${SUDO} -u "\$OWNER_ACCOUNT" test -r "\$FILE" 2>/dev/null; then
    echo "    ok readable by \${OWNER_ACCOUNT}"
  else
    echo "    !! NOT readable by \${OWNER_ACCOUNT} - the service will behave as if the value were unset"
  fi

  if \${SUDO} grep -q 'REPLACE_ME' "\$FILE" 2>/dev/null; then
    echo "    !! REPLACE_ME placeholders remain"
  fi

  for KEY in "\$@"; do
    check_key "\$FILE" "\$KEY"
  done
  echo
}

check_file /etc/happygymstats/api.env       www-data ConnectionStrings__HappyGymStats ProvisionalToken__SigningKey
check_file /etc/happygymstats/api-dev.env   www-data ConnectionStrings__HappyGymStats ProvisionalToken__SigningKey
check_file /etc/happygymstats/blazor.env    www-data Keycloak__ClientSecret
check_file /etc/happygymstats/blazor-dev.env www-data Keycloak__ClientSecret

echo "ENV_CHECK_DONE"
REMOTE

echo
echo "Every '!!' line above is a real problem; see docs/OPERATIONS-PITFALLS.md."
