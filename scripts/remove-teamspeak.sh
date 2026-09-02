#!/usr/bin/env bash
# remove-teamspeak.sh — Decommission the TeamSpeak container and close port 30033.
#
# SCRIPT_CATEGORY=manual-decommission
# SCRIPT_MUTATES_SERVER_STATE=conditional
# SCRIPT_AUTOMATION_SAFE_DEFAULT=1
#
# Default behaviour is a dry run: it surveys what exists and prints exactly what
# would be removed, touching nothing. Removal requires all of:
#   --execute --confirm-remove   and   DEPLOY_REMOVE_TEAMSPEAK=1
#
# Data volumes are treated separately and more carefully than the container.
# A container is trivially recreated from its image; a volume holds the server
# identity, channel tree, permissions and the ServerAdmin token, and deleting it
# is irreversible. So volumes are BACKED UP BY DEFAULT and only deleted when you
# additionally pass --delete-volumes.
#
# Sequence: survey -> backup volumes -> stop -> remove container -> optionally
# remove volumes and image -> verify port 30033 is gone.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: bash scripts/remove-teamspeak.sh [--execute --confirm-remove] [options]

SCRIPT_CATEGORY=manual-decommission
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Removes the TeamSpeak container from the server and frees public port 30033.

Without --execute --confirm-remove (and DEPLOY_REMOVE_TEAMSPEAK=1) this only
surveys and prints a plan. Nothing is stopped, removed or written.

Options:
  --execute              Perform the removal (with --confirm-remove)
  --confirm-remove       Second confirmation flag, required alongside --execute
  --delete-volumes       Also delete the data volumes. IRREVERSIBLE. Without
                         this, volumes are backed up and left in place, so the
                         server can be restored by recreating the container.
  --keep-image           Leave the teamspeak image in the local image store
  --no-backup            Skip the volume backup. Only meaningful with
                         --delete-volumes, and a deliberately bad idea.

Environment:
  DEPLOY_REMOVE_TEAMSPEAK   must be 1 for any mutation (default: 0)
  TS_CONTAINER_NAME         container to target (default: teamspeak-server)
  TS_BACKUP_DIR             where backups are written on the SERVER
                            (default: /var/backups/teamspeak)
  DEPLOY_SSH_HOST/USER/KEY/PROXY_COMMAND  as per scripts/deploy-config.sh

Note on why port 30033 is reachable at all: Docker publishes ports by inserting
iptables rules that are evaluated BEFORE ufw, so a published container port is
internet-reachable even when ufw reports it denied. Removing the container is
what actually closes it; no ufw rule change is needed or sufficient.
EOF
}

EXECUTE=0
CONFIRM=0
DELETE_VOLUMES=0
KEEP_IMAGE=0
DO_BACKUP=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --execute) EXECUTE=1; shift ;;
    --confirm-remove) CONFIRM=1; shift ;;
    --delete-volumes) DELETE_VOLUMES=1; shift ;;
    --keep-image) KEEP_IMAGE=1; shift ;;
    --no-backup) DO_BACKUP=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_REMOVE_TEAMSPEAK:=0}"
: "${TS_CONTAINER_NAME:=teamspeak-server}"
: "${TS_BACKUP_DIR:=/var/backups/teamspeak}"

SSH_OPTS=(-i "${DEPLOY_SSH_KEY}" -o "ProxyCommand=${DEPLOY_PROXY_COMMAND}")
ssh_tty()  { ssh -tt "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }
ssh_pipe() { ssh -T  "${SSH_OPTS[@]}" "${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}" "$@"; }

# ── Survey ────────────────────────────────────────────────────────────────
echo "==> Surveying ${TS_CONTAINER_NAME} on ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
echo "    (read-only; nothing is changed in this phase)"
echo

SURVEY_RC=0
SURVEY="$(ssh_tty "bash -s" <<REMOTE || SURVEY_RC=$?
set -uo pipefail
SUDO=""
if [ "\$(id -u)" != "0" ]; then
  if sudo -n true 2>/dev/null; then SUDO="sudo -n"; else SUDO="sudo"; fi
fi

# Prove docker is actually queryable before trusting an empty container list.
# Without a TTY, interactive sudo cannot prompt, docker fails, and an empty
# result would otherwise read as "the container is not there".
if \${SUDO} docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then
  echo "DOCKER_OK=1"
else
  echo "DOCKER_OK=0"
fi

# grep -c already prints a count; a `|| echo 0` fallback would append a
# SECOND zero and corrupt the parsed value.
_all="\$(\${SUDO} docker ps -a --format '{{.Names}}' 2>/dev/null)"
_run="\$(\${SUDO} docker ps --format '{{.Names}}' 2>/dev/null)"
_exists="\$(printf '%s\\n' "\${_all}" | grep -Fxc '${TS_CONTAINER_NAME}')"
_running="\$(printf '%s\\n' "\${_run}" | grep -Fxc '${TS_CONTAINER_NAME}')"
echo "CONTAINER_EXISTS=\${_exists:-0}"
echo "CONTAINER_RUNNING=\${_running:-0}"
echo "ALL_CONTAINERS=\$(printf '%s' "\${_all}" | tr '\\n' ',')"
echo "SURVEY_OK=1"
echo "---STATUS---"
\${SUDO} docker ps -a --filter "name=^/${TS_CONTAINER_NAME}\$" --format '{{.Names}}\t{{.Status}}\t{{.Image}}\t{{.Ports}}' 2>/dev/null
echo "---IMAGE---"
\${SUDO} docker inspect --format '{{.Config.Image}}' '${TS_CONTAINER_NAME}' 2>/dev/null
echo "---RESTART_POLICY---"
\${SUDO} docker inspect --format '{{.HostConfig.RestartPolicy.Name}}' '${TS_CONTAINER_NAME}' 2>/dev/null
echo "---MOUNTS---"
\${SUDO} docker inspect --format '{{range .Mounts}}{{.Type}}|{{.Name}}|{{.Source}}|{{.Destination}}{{"\n"}}{{end}}' '${TS_CONTAINER_NAME}' 2>/dev/null
echo "---COMPOSE_PROJECT---"
\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' '${TS_CONTAINER_NAME}' 2>/dev/null
echo "---COMPOSE_FILE---"
\${SUDO} docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' '${TS_CONTAINER_NAME}' 2>/dev/null
echo "---PORT30033---"
ss -ltn 2>/dev/null | grep ':30033 ' || echo "(not listening)"
echo "---SYSTEMD_UNITS---"
systemctl list-unit-files 2>/dev/null | grep -i teamspeak || echo "(no teamspeak systemd unit)"
echo "---DISK---"
df -h /var 2>/dev/null | tail -1
REMOTE
)"

echo "${SURVEY}" | sed 's/^/    /'
echo

# Distinguish "the host said there is no container" from "we never reached the
# host". Treating an unreachable host as "nothing to do" would report port 30033
# as closed when it is still wide open.
if ! echo "${SURVEY}" | grep -q '^SURVEY_OK=1'; then
  echo "REMOVE_TEAMSPEAK_FAIL category=survey_unreachable ssh_rc=${SURVEY_RC}" >&2
  echo "    The survey did not complete, so nothing is known about the container" >&2
  echo "    or about port 30033. This is NOT the same as 'already removed'." >&2
  echo "    Most likely the Cloudflare Access session has expired:" >&2
  echo "      cloudflared access login https://ssh.geromet.com" >&2
  exit 1
fi

docker_ok="$(echo "${SURVEY}" | grep '^DOCKER_OK=' | head -1 | cut -d= -f2 | tr -dc '0-9')"
if [[ "${docker_ok:-0}" != "1" ]]; then
  echo "REMOVE_TEAMSPEAK_FAIL category=docker_not_queryable" >&2
  echo "    Reached the host, but 'docker' could not be run — most likely sudo" >&2
  echo "    needed a password and had no terminal to ask on." >&2
  echo "    An empty container list in that state means 'could not look', NOT" >&2
  echo "    'nothing is there', so this stops rather than reporting success." >&2
  echo "    Run 'sudo -v' on the host first, or add your user to the docker group." >&2
  exit 1
fi

exists="$(echo "${SURVEY}" | grep '^CONTAINER_EXISTS=' | head -1 | cut -d= -f2 | tr -dc '0-9')"
if [[ "${exists:-0}" == "0" ]]; then
  echo "==> No container named '${TS_CONTAINER_NAME}' found on the host."
  echo "    Containers docker does report: $(echo "${SURVEY}" | grep '^ALL_CONTAINERS=' | head -1 | cut -d= -f2-)"
  echo "    Nothing to remove. If 30033 is still listening, something else owns it —"
  echo "    run: bash scripts/recon-fetch.sh ports --sudo"
  echo "    If you expected TeamSpeak here, check TS_CONTAINER_NAME."
  exit 0
fi

compose_project="$(echo "${SURVEY}" | sed -n '/---COMPOSE_PROJECT---/,/---COMPOSE_FILE---/p' | sed '1d;$d' | tr -d ' \r')"
volumes="$(echo "${SURVEY}" | sed -n '/---MOUNTS---/,/---COMPOSE_PROJECT---/p' | sed '1d;$d' | grep '^volume|' | cut -d'|' -f2 | grep -v '^$' || true)"

echo "==> Plan"
echo "    1. Back up data volumes to ${TS_BACKUP_DIR} on the server ($( ((DO_BACKUP)) && echo enabled || echo SKIPPED))"
echo "    2. Stop container ${TS_CONTAINER_NAME}"
echo "    3. Remove container ${TS_CONTAINER_NAME}"
echo "    4. Volumes: $( ((DELETE_VOLUMES)) && echo 'DELETE (irreversible)' || echo 'keep in place' )"
echo "    5. Image: $( ((KEEP_IMAGE)) && echo 'keep' || echo 'remove if unused' )"
echo "    6. Verify port 30033 is no longer listening"
echo
if [[ -n "${volumes}" ]]; then
  echo "    named volumes detected:"
  echo "${volumes}" | sed 's/^/      - /'
else
  echo "    named volumes detected: none (data may be in a bind mount — see MOUNTS above)"
fi
echo

if [[ -n "${compose_project}" && "${compose_project}" != "<no value>" ]]; then
  echo "    !! This container is managed by docker compose (project: ${compose_project})."
  echo "    !! Removing it directly will leave the compose file in place, and a later"
  echo "    !! 'docker compose up' would recreate it. Remove the service from the"
  echo "    !! compose file as well, or this comes back."
  echo
fi

restart_policy="$(echo "${SURVEY}" | sed -n '/---RESTART_POLICY---/,/---MOUNTS---/p' | sed '1d;$d' | tr -d ' \r')"
if [[ "${restart_policy}" == "always" || "${restart_policy}" == "unless-stopped" ]]; then
  echo "    note: restart policy is '${restart_policy}'. Stopping is not enough on its"
  echo "    own across a daemon restart — the container must be removed, which this does."
  echo
fi

# ── Gating ────────────────────────────────────────────────────────────────
if [[ "${DEPLOY_REMOVE_TEAMSPEAK}" != "1" ]]; then
  echo "==> DRY RUN — nothing was changed."
  echo "    Set DEPLOY_REMOVE_TEAMSPEAK=1 to enable mutation."
  exit 0
fi

if (( EXECUTE != 1 || CONFIRM != 1 )); then
  echo "==> DRY RUN — nothing was changed."
  echo "    To remove, re-run with: --execute --confirm-remove"
  exit 0
fi

if (( DELETE_VOLUMES == 1 && DO_BACKUP == 0 )); then
  echo "!! --delete-volumes with --no-backup destroys the TeamSpeak server identity," >&2
  echo "!! channel tree and permissions with no way back. Refusing." >&2
  echo "!! Drop --no-backup, or accept the backup and delete it yourself afterwards." >&2
  exit 1
fi

# ── Execute ───────────────────────────────────────────────────────────────
echo "==> Executing removal"

ssh_tty "bash -s" <<REMOTE
set -euo pipefail
SUDO=""
if [ "\$(id -u)" != "0" ]; then
  if sudo -n true 2>/dev/null; then SUDO="sudo -n"; else SUDO="sudo"; fi
fi

CONTAINER='${TS_CONTAINER_NAME}'
BACKUP_DIR='${TS_BACKUP_DIR}'
DO_BACKUP=${DO_BACKUP}
DELETE_VOLUMES=${DELETE_VOLUMES}
KEEP_IMAGE=${KEEP_IMAGE}

IMAGE="\$(\${SUDO} docker inspect --format '{{.Config.Image}}' "\${CONTAINER}" 2>/dev/null || echo '')"
VOLUMES="\$(\${SUDO} docker inspect --format '{{range .Mounts}}{{if eq .Type "volume"}}{{.Name}} {{end}}{{end}}' "\${CONTAINER}" 2>/dev/null || echo '')"
BINDS="\$(\${SUDO} docker inspect --format '{{range .Mounts}}{{if eq .Type "bind"}}{{.Source}} {{end}}{{end}}' "\${CONTAINER}" 2>/dev/null || echo '')"

if [ "\${DO_BACKUP}" = "1" ]; then
  echo "--> Backing up to \${BACKUP_DIR}"
  \${SUDO} mkdir -p "\${BACKUP_DIR}"
  STAMP="\$(date -u +%Y%m%dT%H%M%SZ)"

  for v in \${VOLUMES}; do
    echo "    volume \${v}"
    # Read the volume through a throwaway container so the backup works
    # regardless of where the driver stores it on disk.
    \${SUDO} docker run --rm \
      -v "\${v}":/from:ro \
      -v "\${BACKUP_DIR}":/to \
      alpine:3 \
      tar -czf "/to/\${CONTAINER}-\${v}-\${STAMP}.tar.gz" -C /from . \
      && echo "      -> \${BACKUP_DIR}/\${CONTAINER}-\${v}-\${STAMP}.tar.gz" \
      || echo "      !! backup FAILED for \${v}"
  done

  for b in \${BINDS}; do
    if [ -d "\${b}" ]; then
      safe="\$(echo "\${b}" | tr '/' '_')"
      echo "    bind mount \${b}"
      \${SUDO} tar -czf "\${BACKUP_DIR}/\${CONTAINER}-bind\${safe}-\${STAMP}.tar.gz" -C "\${b}" . \
        && echo "      -> \${BACKUP_DIR}/\${CONTAINER}-bind\${safe}-\${STAMP}.tar.gz" \
        || echo "      !! backup FAILED for \${b}"
    fi
  done

  if [ -z "\${VOLUMES}\${BINDS}" ]; then
    echo "    (no volumes or bind mounts to back up)"
  fi
  echo "--> Backups present:"
  \${SUDO} ls -lh "\${BACKUP_DIR}" 2>/dev/null | tail -10
else
  echo "--> Backup skipped (--no-backup)"
fi

echo "--> Stopping \${CONTAINER}"
\${SUDO} docker stop "\${CONTAINER}" >/dev/null 2>&1 || echo "    (already stopped)"

echo "--> Removing container \${CONTAINER}"
\${SUDO} docker rm "\${CONTAINER}" >/dev/null 2>&1 || echo "    (already removed)"

if [ "\${DELETE_VOLUMES}" = "1" ]; then
  for v in \${VOLUMES}; do
    echo "--> Deleting volume \${v}"
    \${SUDO} docker volume rm "\${v}" >/dev/null 2>&1 && echo "    deleted" || echo "    !! could not delete \${v} (still in use?)"
  done
else
  if [ -n "\${VOLUMES}" ]; then
    echo "--> Keeping volumes (recreate the container to restore the server):"
    for v in \${VOLUMES}; do echo "      \${v}"; done
  fi
fi

if [ "\${KEEP_IMAGE}" != "1" ] && [ -n "\${IMAGE}" ]; then
  echo "--> Removing image \${IMAGE} if unused"
  \${SUDO} docker rmi "\${IMAGE}" >/dev/null 2>&1 && echo "    removed" || echo "    (still referenced, or already gone)"
fi

echo "--> Verifying port 30033"
if ss -ltn 2>/dev/null | grep -q ':30033 '; then
  echo "    !! 30033 is STILL listening:"
  ss -ltn 2>/dev/null | grep ':30033 '
  echo "    !! Something else owns it. Investigate before assuming it is closed."
  exit 1
else
  echo "    30033 is no longer listening"
fi

echo "--> Remaining containers:"
\${SUDO} docker ps --format '    {{.Names}}\t{{.Status}}\t{{.Ports}}' 2>/dev/null
REMOTE

cat <<EOF

==> TeamSpeak removal complete

Follow-ups:
  - Re-run the ports collector to confirm exposure from the host's own view:
      bash scripts/recon-fetch.sh ports --sudo
  - If a compose file still defines this service, remove it there too or the
    next 'docker compose up' recreates the container.
  - Backups (if taken) are on the SERVER under ${TS_BACKUP_DIR}. They are not
    copied here. Delete them once you are sure TeamSpeak is not coming back.
  - Cloudflare DNS: if any record pointed at TeamSpeak, retire it.
EOF
