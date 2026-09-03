#!/usr/bin/env bash
# upgrade-containers.sh — Upgrade the Keycloak and PostgreSQL containers.
#
# SCRIPT_CATEGORY=manual-maintenance
# SCRIPT_MUTATES_SERVER_STATE=conditional
# SCRIPT_AUTOMATION_SAFE_DEFAULT=1
#
# Survey-and-plan by default. Mutating requires --execute --confirm-upgrade AND
# DEPLOY_UPGRADE_CONTAINERS=1.
#
# WHY, briefly:
#   Keycloak 26.0 is end-of-life and predates the CVE-2026-18963 fix (CVSS 9.1,
#   unauthenticated account takeover via the reset-credentials flow, fixed in
#   26.7.2). Every authorization decision in this stack — the admin-only dev
#   host, AdminPanel — is only as strong as Keycloak. 26.7.2 also fixes
#   CVE-2026-17048 (admin API vault secret leak) and CVE-2026-14613
#   (fine-grained admin permission bypass), neither of which depends on the
#   reset flow being enabled.
#
#   PostgreSQL before 16.15 carries CVE-2026-14664 / 14669 / 14663 / 6473. All
#   need an authenticated database user, and the server is loopback-bound with
#   no published port, so this is escalation rather than entry — lower urgency,
#   same maintenance window.
#
# TWO RULES THIS SCRIPT ENFORCES:
#
#   1. Back up before touching anything. Keycloak's realm config and Postgres's
#      data are not reconstructible from this repo. A pg_dumpall runs first and
#      the upgrade aborts if it fails or looks empty.
#
#   2. Never change a PostgreSQL MAJOR version in place. Postgres refuses to
#      start on a data directory written by a different major, and the failure
#      arrives after the old container is gone. Only minor upgrades (16.x ->
#      16.y) are performed; a major bump is refused with instructions.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: bash scripts/upgrade-containers.sh [--execute --confirm-upgrade] [options]

SCRIPT_CATEGORY=manual-maintenance
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Surveys and (when confirmed) upgrades the Keycloak and PostgreSQL containers.

Default is a dry run: it reports current versions, the target versions, whether
the stack is docker-compose managed, and the exact plan. Nothing is pulled,
stopped or changed.

Options:
  --execute            Perform the upgrade (with --confirm-upgrade)
  --confirm-upgrade    Second confirmation flag
  --target keycloak|postgres|all     (default: all)
  --keycloak-version X               (default: 26.7.2)
  --postgres-version X               (default: 16.15)
  --skip-backup        Refused for postgres. Only honoured for a Keycloak-only
                       run where the database is untouched.
  --rollback-info      Print how to roll back, then exit

Environment:
  DEPLOY_UPGRADE_CONTAINERS   must be 1 for any mutation (default: 0)
  UPGRADE_BACKUP_DIR          on the SERVER (default: /var/backups/happygymstats)
  DEPLOY_SSH_HOST/USER/KEY/PROXY_COMMAND   as per scripts/deploy-config.sh

Expect downtime: Keycloak restart signs everyone out; a Postgres restart drops
the API's connections until it reconnects. Minutes, not seconds.

After upgrading, confirm with:
  bash scripts/recon-fetch.sh security --sudo
EOF
}

rollback_info() {
  cat <<'EOF'
Rollback

  Both services are containers, so rollback is re-running the previous image
  tag. The script prints the exact prior tag and image digest before changing
  anything — keep that output.

  Keycloak:
    docker stop <container> && docker rm <container>
    # recreate with the PREVIOUS tag, then restart
    # if the realm was migrated by the newer version, restore the dump first:
    #   docker exec -i <pg-container> psql -U <user> -d keycloak < <backup>.sql

    Keycloak migrates its database schema forward on first start with a new
    version, and that migration is NOT reversible. Rolling Keycloak back means
    restoring the database dump as well, not just the image tag.

  PostgreSQL (minor, e.g. 16.14 -> 16.15):
    Minor upgrades keep the on-disk format, so reverting the tag is enough.

  PostgreSQL (major, e.g. 16 -> 17):
    Not performed by this script. The data directory is not compatible across
    majors and the old container is gone by the time it fails. Do it as a
    dump/restore into a fresh volume, with the old volume retained until the
    new one is verified.
EOF
}

TARGET="all"
EXECUTE=0
CONFIRM=0
SKIP_BACKUP=0
KEYCLOAK_VERSION="26.7.2"
POSTGRES_VERSION="16.15"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --execute) EXECUTE=1; shift ;;
    --confirm-upgrade) CONFIRM=1; shift ;;
    --target) TARGET="${2:-all}"; shift 2 ;;
    --keycloak-version) KEYCLOAK_VERSION="${2:-}"; shift 2 ;;
    --postgres-version) POSTGRES_VERSION="${2:-}"; shift 2 ;;
    --skip-backup) SKIP_BACKUP=1; shift ;;
    --rollback-info) rollback_info; exit 0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

case "${TARGET}" in
  keycloak|postgres|all) ;;
  *) echo "Invalid --target: ${TARGET}" >&2; exit 1 ;;
esac

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_UPGRADE_CONTAINERS:=0}"
: "${UPGRADE_BACKUP_DIR:=/var/backups/happygymstats}"

# SSH is handled by remote_exec_script; do not add a raw `ssh ... bash -s`
# helper here — that pattern feeds the script to sudo as passwords.
# shellcheck source=lib/remote-exec.sh
source "${SCRIPT_DIR}/lib/remote-exec.sh"

# ── Survey ────────────────────────────────────────────────────────────────
echo "==> Surveying containers on ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
echo "    (read-only; nothing is changed in this phase)"
echo

# Command substitution captures stdout, which is where sudo writes its password
# prompt — so "$(ssh ...)" hides the prompt and the run hangs waiting for input
# the operator was never shown. Stream through tee instead: the prompt reaches
# the terminal, and the survey is read back from the file.
SURVEY_TMP="$(mktemp)"
trap 'rm -f "${SURVEY_TMP}"' EXIT
SURVEY_RC=0
remote_exec_script --tee "${SURVEY_TMP}" --indent <<'REMOTE' || SURVEY_RC=$?
set -uo pipefail
# SUDO is supplied by remote_exec_script's preamble (already authenticated,
# pinned to `sudo -n`). Do NOT redefine it here.

if ${SUDO} docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then
  echo "DOCKER_OK=1"
else
  echo "DOCKER_OK=0"
fi

KC="$(${SUDO} docker ps --format '{{.Names}}' 2>/dev/null | grep -i keycloak | head -1)"
PG="$(${SUDO} docker ps --format '{{.Names}}' 2>/dev/null | grep -i postgres | head -1)"
echo "KC_NAME=${KC}"
echo "PG_NAME=${PG}"
[ -n "${KC}" ] && echo "KC_IMAGE=$(${SUDO} docker inspect --format '{{.Config.Image}}' "${KC}" 2>/dev/null)"
[ -n "${PG}" ] && echo "PG_IMAGE=$(${SUDO} docker inspect --format '{{.Config.Image}}' "${PG}" 2>/dev/null)"
[ -n "${KC}" ] && echo "KC_COMPOSE=$(${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "${KC}" 2>/dev/null)"
[ -n "${PG}" ] && echo "PG_COMPOSE=$(${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "${PG}" 2>/dev/null)"
[ -n "${KC}" ] && echo "KC_COMPOSE_FILE=$(${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "${KC}" 2>/dev/null)"
[ -n "${PG}" ] && echo "PG_VERSION_RUNNING=$(${SUDO} docker exec "${PG}" postgres --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+' | head -1)"
[ -n "${PG}" ] && echo "PG_VOLUMES=$(${SUDO} docker inspect --format '{{range .Mounts}}{{if eq .Type "volume"}}{{.Name}} {{end}}{{end}}' "${PG}" 2>/dev/null)"
[ -n "${PG}" ] && echo "PG_DBS=$(${SUDO} docker exec "${PG}" psql -U postgres -Atc 'SELECT datname FROM pg_database WHERE NOT datistemplate' 2>/dev/null | tr '\n' ',')"
echo "DISK_AVAIL=$(df -BG --output=avail /var 2>/dev/null | tail -1 | tr -dc '0-9')"
echo "SURVEY_OK=1"
REMOTE

# Drop the sudo prompt line and CRs left by the forced TTY.
sed -i 's/\r$//' "${SURVEY_TMP}" 2>/dev/null || true
sed -i '/^\[sudo\] password for /d' "${SURVEY_TMP}" 2>/dev/null || true
SURVEY="$(cat "${SURVEY_TMP}")"

echo

get() { echo "${SURVEY}" | grep "^$1=" | head -1 | cut -d= -f2- | tr -d '\r'; }

# Distinguish a refused sudo from an unreachable host: blaming Cloudflare for
# a wrong password sends the operator to fix the wrong thing.
if grep -q 'REMOTE_SUDO_FAILED\|Sorry, try again' "${SURVEY_TMP}" 2>/dev/null; then
  echo "UPGRADE_FAIL category=sudo_auth_failed" >&2
  echo "    sudo on the server refused the password, so nothing ran." >&2
  echo "    Re-run and enter it when prompted." >&2
  exit 1
fi

if ! echo "${SURVEY}" | grep -q '^SURVEY_OK=1'; then
  echo "UPGRADE_FAIL category=survey_unreachable ssh_rc=${SURVEY_RC}" >&2
  echo "    Nothing is known about the host, which is NOT the same as 'nothing to do'." >&2
  echo "    Most likely: cloudflared access login https://ssh.geromet.com" >&2
  exit 1
fi
if [[ "$(get DOCKER_OK)" != "1" ]]; then
  echo "UPGRADE_FAIL category=docker_not_queryable" >&2
  echo "    Reached the host but could not run docker — sudo probably needed a" >&2
  echo "    password with no terminal to ask on. Refusing to guess." >&2
  exit 1
fi

KC_NAME="$(get KC_NAME)";   KC_IMAGE="$(get KC_IMAGE)"
PG_NAME="$(get PG_NAME)";   PG_IMAGE="$(get PG_IMAGE)"
PG_RUNNING="$(get PG_VERSION_RUNNING)"
KC_COMPOSE="$(get KC_COMPOSE)"; PG_COMPOSE="$(get PG_COMPOSE)"
COMPOSE_FILE="$(get KC_COMPOSE_FILE)"
DISK_AVAIL="$(get DISK_AVAIL)"

# ── Guard: never bump a Postgres major in place ───────────────────────────
if [[ "${TARGET}" == "postgres" || "${TARGET}" == "all" ]] && [[ -n "${PG_RUNNING}" ]]; then
  cur_major="${PG_RUNNING%%.*}"
  new_major="${POSTGRES_VERSION%%.*}"
  if [[ "${cur_major}" != "${new_major}" ]]; then
    echo "UPGRADE_FAIL category=postgres_major_change current=${PG_RUNNING} requested=${POSTGRES_VERSION}" >&2
    echo >&2
    echo "    PostgreSQL will not start on a data directory written by a different" >&2
    echo "    major version, and that failure lands after the old container is gone." >&2
    echo "    This script only performs MINOR upgrades (${cur_major}.x -> ${cur_major}.y)." >&2
    echo >&2
    echo "    For a major upgrade, dump and restore into a fresh volume, keeping the" >&2
    echo "    old volume until the new one is verified. See --rollback-info." >&2
    exit 1
  fi
fi

# ── Plan ──────────────────────────────────────────────────────────────────
echo "==> Plan"
if [[ "${TARGET}" == "keycloak" || "${TARGET}" == "all" ]]; then
  if [[ -n "${KC_NAME}" ]]; then
    echo "    Keycloak: ${KC_IMAGE}  ->  quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}"
    echo "              container ${KC_NAME}"
  else
    echo "    Keycloak: no running container found — skipping"
  fi
fi
if [[ "${TARGET}" == "postgres" || "${TARGET}" == "all" ]]; then
  if [[ -n "${PG_NAME}" ]]; then
    echo "    Postgres: ${PG_IMAGE} (running ${PG_RUNNING:-unknown})  ->  postgres:${POSTGRES_VERSION}"
    echo "              container ${PG_NAME}"
  else
    echo "    Postgres: no running container found — skipping"
  fi
fi
echo
echo "    Backup first: $( ((SKIP_BACKUP)) && echo 'SKIPPED (--skip-backup)' || echo "yes, to ${UPGRADE_BACKUP_DIR} on the server" )"
echo "    Disk available on /var: ${DISK_AVAIL:-unknown} GB"
echo

if [[ -n "${KC_COMPOSE}" && "${KC_COMPOSE}" != "<no value>" ]] || [[ -n "${PG_COMPOSE}" && "${PG_COMPOSE}" != "<no value>" ]]; then
  cat <<EOF
    !! These containers are managed by docker compose (project: ${KC_COMPOSE:-${PG_COMPOSE}}).
    !! compose file: ${COMPOSE_FILE:-unknown}
    !!
    !! Recreating them directly leaves the compose file pinning the OLD tags, so
    !! the next 'docker compose up' silently downgrades you back to the vulnerable
    !! version. Edit the image tags in that file as part of this change.
    !! With the file updated, the cleanest upgrade is:
    !!     docker compose pull && docker compose up -d
EOF
  echo
fi

if [[ "${SKIP_BACKUP}" == "1" && ( "${TARGET}" == "postgres" || "${TARGET}" == "all" ) ]]; then
  echo "UPGRADE_FAIL category=refusing_skip_backup" >&2
  echo "    --skip-backup is not honoured when Postgres is in scope. Keycloak's realm" >&2
  echo "    config and the application data live in that database and are not" >&2
  echo "    reconstructible from this repository." >&2
  exit 1
fi

# ── Gating ────────────────────────────────────────────────────────────────
if [[ "${DEPLOY_UPGRADE_CONTAINERS}" != "1" ]]; then
  echo "==> DRY RUN — nothing was changed."
  echo "    Set DEPLOY_UPGRADE_CONTAINERS=1 to enable mutation."
  exit 0
fi
if (( EXECUTE != 1 || CONFIRM != 1 )); then
  echo "==> DRY RUN — nothing was changed."
  echo "    To upgrade, re-run with: --execute --confirm-upgrade"
  exit 0
fi

# ── Execute ───────────────────────────────────────────────────────────────
echo "==> Executing upgrade"
echo "    Record these for rollback:"
echo "      Keycloak image was: ${KC_IMAGE:-n/a}"
echo "      Postgres image was: ${PG_IMAGE:-n/a}"
echo

remote_exec_script <<REMOTE
set -euo pipefail
# SUDO is supplied by remote_exec_script's preamble (already authenticated,
# pinned to `sudo -n`). Do NOT redefine it here.

TARGET='${TARGET}'
KC_NAME='${KC_NAME}'
PG_NAME='${PG_NAME}'
KC_IMAGE='${KC_IMAGE}'
PG_IMAGE='${PG_IMAGE}'
KC_NEW='quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}'
PG_NEW='postgres:${POSTGRES_VERSION}'
BACKUP_DIR='${UPGRADE_BACKUP_DIR}'
SKIP_BACKUP=${SKIP_BACKUP}
STAMP="\$(date -u +%Y%m%dT%H%M%SZ)"

# ---- Backup -------------------------------------------------------------
if [ "\${SKIP_BACKUP}" != "1" ] && [ -n "\${PG_NAME}" ]; then
  echo "--> Dumping all databases before any change"
  \${SUDO} mkdir -p "\${BACKUP_DIR}"
  DUMP="\${BACKUP_DIR}/pg_dumpall-\${STAMP}.sql"
  if \${SUDO} docker exec "\${PG_NAME}" pg_dumpall -U postgres > /tmp/_pgdump.\$\$ 2>/tmp/_pgdumperr.\$\$; then
    \${SUDO} mv /tmp/_pgdump.\$\$ "\${DUMP}"
    \${SUDO} chmod 600 "\${DUMP}"
    SIZE="\$(\${SUDO} stat -c %s "\${DUMP}" 2>/dev/null || echo 0)"
    echo "    wrote \${DUMP} (\${SIZE} bytes)"
    # A dump that is suspiciously small usually means the auth failed and we
    # captured an error page. Upgrading on top of that would be unrecoverable.
    if [ "\${SIZE}" -lt 1024 ]; then
      echo "!! Dump is only \${SIZE} bytes — that is not a real backup." >&2
      cat /tmp/_pgdumperr.\$\$ >&2 || true
      echo "!! Aborting before anything is changed." >&2
      exit 1
    fi
  else
    echo "!! pg_dumpall FAILED — aborting before anything is changed." >&2
    cat /tmp/_pgdumperr.\$\$ >&2 || true
    rm -f /tmp/_pgdump.\$\$ /tmp/_pgdumperr.\$\$
    exit 1
  fi
  rm -f /tmp/_pgdumperr.\$\$
else
  echo "--> Backup skipped"
fi

# ---- Pull new images first ----------------------------------------------
# Pull before stopping anything: a failed pull then costs no downtime.
if [ "\${TARGET}" = "keycloak" ] || [ "\${TARGET}" = "all" ]; then
  if [ -n "\${KC_NAME}" ]; then
    echo "--> Pulling \${KC_NEW}"
    \${SUDO} docker pull "\${KC_NEW}"
  fi
fi
if [ "\${TARGET}" = "postgres" ] || [ "\${TARGET}" = "all" ]; then
  if [ -n "\${PG_NAME}" ]; then
    echo "--> Pulling \${PG_NEW}"
    \${SUDO} docker pull "\${PG_NEW}"
  fi
fi

# ---- Recreate -----------------------------------------------------------
# Uses the container's own configuration as the source of truth via
# 'docker run' reconstruction is error-prone, so prefer compose when present.
recreate() {
  local name="\$1" new_image="\$2"
  local compose_project compose_file service
  compose_project="\$(\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "\${name}" 2>/dev/null)"
  compose_file="\$(\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "\${name}" 2>/dev/null)"
  service="\$(\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "\${name}" 2>/dev/null)"

  if [ -n "\${compose_project}" ] && [ "\${compose_project}" != "<no value>" ] && [ -f "\${compose_file}" ]; then
    echo "    \${name} is compose-managed (service \${service}, file \${compose_file})"
    echo "    !! Update the image tag to \${new_image} in that file, then run:"
    echo "    !!   \${SUDO} docker compose -f '\${compose_file}' pull \${service}"
    echo "    !!   \${SUDO} docker compose -f '\${compose_file}' up -d \${service}"
    echo "    !! Not doing it automatically: editing your compose file is your call,"
    echo "    !! and a direct recreate would be reverted by the next 'compose up'."
    return 2
  fi

  echo "    \${name} is a standalone container; recreating with \${new_image}"
  echo "    !! Standalone recreation must reproduce every flag the original had"
  echo "    !! (env, volumes, ports, network). Printing the original config so you"
  echo "    !! can verify before it is applied:"
  \${SUDO} docker inspect "\${name}" --format '{{json .Config.Env}}' 2>/dev/null | tr ',' '\n' | sed 's/^/      /' | head -30
  echo "    !! Automatic standalone recreation is deliberately NOT performed."
  return 2
}

RC=0
if [ "\${TARGET}" = "keycloak" ] || [ "\${TARGET}" = "all" ]; then
  if [ -n "\${KC_NAME}" ]; then
    echo "--> Keycloak"
    recreate "\${KC_NAME}" "\${KC_NEW}" || RC=2
  fi
fi
if [ "\${TARGET}" = "postgres" ] || [ "\${TARGET}" = "all" ]; then
  if [ -n "\${PG_NAME}" ]; then
    echo "--> Postgres"
    recreate "\${PG_NAME}" "\${PG_NEW}" || RC=2
  fi
fi

echo
echo "--> Images are pulled and a verified backup exists."
if [ "\${RC}" = "2" ]; then
  echo "--> Final recreate step is left to you, per the instructions above."
fi
exit 0
REMOTE

cat <<EOF

==> Upgrade preparation complete

  New images are pulled on the host and a verified pg_dumpall exists under
  ${UPGRADE_BACKUP_DIR}. The recreate step is intentionally left to you:
  reproducing a container's full configuration from the outside is exactly the
  kind of thing that quietly drops an env var or a volume.

  Rollback details:  bash scripts/upgrade-containers.sh --rollback-info

  After recreating, verify:
    bash scripts/recon-fetch.sh security --sudo
      -> Keycloak version should no longer raise CVE-2026-18963
    curl -fsS http://127.0.0.1:8080/realms/torn/.well-known/openid-configuration
      -> run on the host; confirms the realm still resolves
    Sign in to the AdminPanel to confirm the realm survived the migration.

  Note: Keycloak migrates its database schema forward on first start with a new
  version and that migration is not reversible. Rolling back means restoring the
  dump, not just re-tagging the image.
EOF
