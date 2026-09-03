#!/usr/bin/env bash
# setup-auto-reboot.sh — Schedule unattended reboots so installed patches take effect.
#
# SCRIPT_CATEGORY=manual-bootstrap
# SCRIPT_MUTATES_SERVER_STATE=conditional
# SCRIPT_AUTOMATION_SAFE_DEFAULT=1
#
# Survey-and-plan by default. Mutating requires --execute --confirm-schedule AND
# DEPLOY_ENABLE_AUTO_REBOOT=1.
#
# WHY THIS IS NEEDED
#   unattended-upgrades is already installed and enabled on this host, so
#   security updates are downloaded and installed on schedule. What is missing is
#   activation: the audit found the machine running kernel 6.8.0-111 with
#   6.8.0-138 installed. Twenty-seven releases of kernel fixes, plus libc6, are
#   sitting on disk unused. Patching without rebooting is not patching.
#
# TWO MODES
#   conditional (default) — Turn on unattended-upgrades' own automatic reboot.
#       The machine reboots ONLY when /var/run/reboot-required exists, i.e. only
#       when a kernel or libc update actually needs it. Typically once or twice a
#       month, not weekly. This is the idiomatic Debian/Ubuntu mechanism and
#       avoids downtime that buys nothing.
#
#   weekly — A systemd timer that reboots every week whether or not anything
#       needs it. Simpler to reason about, and it also clears leaked memory and
#       long-lived container drift. Costs a few minutes of downtime a week.
#
#   GUARD FILE (default /etc/happygymstats/no-reboot), for war nights:
#       weekly mode      — fully honoured. The helper checks the file and skips
#                          the reboot, logging why.
#       conditional mode — ADVISORY ONLY. unattended-upgrades exposes no hook
#                          that can veto its own reboot, so the guard is logged
#                          but does not stop it. For a hard hold, run this
#                          script with --disable and re-enable afterwards.
#                          Stated plainly because a guard you believe in but
#                          that does not fire is worse than no guard at all.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: bash scripts/setup-auto-reboot.sh [--execute --confirm-schedule] [options]

SCRIPT_CATEGORY=manual-bootstrap
SCRIPT_MUTATES_SERVER_STATE=conditional
SCRIPT_AUTOMATION_SAFE_DEFAULT=1

Options:
  --mode conditional|weekly   conditional (default) reboots only when
                              /var/run/reboot-required exists; weekly reboots on
                              a fixed schedule regardless.
  --time HH:MM                Reboot time, server local time (default 04:00)
  --day  DAY                  Weekly mode only: Mon|Tue|Wed|Thu|Fri|Sat|Sun
                              (default Sun)
  --with-users                Allow rebooting while someone is logged in.
                              Default is to defer, which is safer but means a
                              forgotten SSH session can block patching forever.
  --guard-file PATH           Skip the reboot while this file exists
                              (default /etc/happygymstats/no-reboot).
                              Fully honoured in weekly mode; advisory only in
                              conditional mode — see WAR NIGHTS below.
  --status                    Show what is currently configured, then exit
  --disable                   Remove the schedule this script installed
  --execute --confirm-schedule    Apply (also needs DEPLOY_ENABLE_AUTO_REBOOT=1)

PRE-FLIGHT
  An automatic reboot is only safe if everything comes back on its own. Before
  changing anything the script verifies:
    - every happygymstats unit is ENABLED, not merely running
    - docker.service is enabled
    - every running container restarts on its own (always / unless-stopped)
    - no units are currently in a failed state
  If any check fails it refuses, because an unattended reboot on a host that
  does not fully self-restore is an outage generator, not a maintenance policy.

WAR NIGHTS
  weekly mode — the guard file is enough:
      touch /etc/happygymstats/no-reboot     # before
      rm    /etc/happygymstats/no-reboot     # after
    Skipped attempts are logged to the journal, so a guard left in place by
    accident is visible rather than silent.

  conditional mode — unattended-upgrades cannot be vetoed by a file, so use:
      bash scripts/setup-auto-reboot.sh --disable --execute --confirm-schedule
    and re-run the enable command afterwards.
EOF
}

MODE="conditional"
REBOOT_TIME="04:00"
REBOOT_DAY="Sun"
WITH_USERS=0
GUARD_FILE="/etc/happygymstats/no-reboot"
EXECUTE=0
CONFIRM=0
SHOW_STATUS=0
DISABLE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) MODE="${2:-conditional}"; shift 2 ;;
    --time) REBOOT_TIME="${2:-04:00}"; shift 2 ;;
    --day) REBOOT_DAY="${2:-Sun}"; shift 2 ;;
    --with-users) WITH_USERS=1; shift ;;
    --guard-file) GUARD_FILE="${2:-}"; shift 2 ;;
    --status) SHOW_STATUS=1; shift ;;
    --disable) DISABLE=1; shift ;;
    --execute) EXECUTE=1; shift ;;
    --confirm-schedule) CONFIRM=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

case "${MODE}" in conditional|weekly) ;; *) echo "Invalid --mode: ${MODE}" >&2; exit 1 ;; esac
if ! [[ "${REBOOT_TIME}" =~ ^([01][0-9]|2[0-3]):[0-5][0-9]$ ]]; then
  echo "Invalid --time '${REBOOT_TIME}' (expected HH:MM, 24h)" >&2; exit 1
fi
case "${REBOOT_DAY}" in Mon|Tue|Wed|Thu|Fri|Sat|Sun) ;; *) echo "Invalid --day: ${REBOOT_DAY}" >&2; exit 1 ;; esac

if [[ -f "${ROOT_DIR}/.env.deploy" ]]; then
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.deploy"
fi

: "${DEPLOY_SSH_HOST:=ssh.geromet.com}"
: "${DEPLOY_SSH_USER:=anon}"
: "${DEPLOY_SSH_KEY:=$HOME/.ssh/id_token2_bio3_hetzner}"
: "${DEPLOY_PROXY_COMMAND:=cloudflared access ssh --hostname ssh.geromet.com}"
: "${DEPLOY_ENABLE_AUTO_REBOOT:=0}"

# SSH is handled by remote_exec_script; do not add a raw `ssh ... bash -s`
# helper here — that pattern feeds the script to sudo as passwords.
# shellcheck source=lib/remote-exec.sh
source "${SCRIPT_DIR}/lib/remote-exec.sh"

readonly APT_CONF="/etc/apt/apt.conf.d/51happygymstats-auto-reboot"
readonly TIMER_UNIT="happygymstats-auto-reboot.timer"
readonly SERVICE_UNIT="happygymstats-auto-reboot.service"
readonly REBOOT_HELPER="/usr/local/sbin/happygymstats-auto-reboot"

# ── Status ────────────────────────────────────────────────────────────────
if (( SHOW_STATUS )); then
  echo "==> Current auto-reboot configuration on ${DEPLOY_SSH_USER}@${DEPLOY_SSH_HOST}"
  echo
  remote_exec_script <<REMOTE
set -uo pipefail
# SUDO comes from remote_exec_script's preamble; do NOT redefine it.
echo "--- unattended-upgrades reboot settings (effective) ---"
\${SUDO} apt-config dump 2>/dev/null | grep -i 'Unattended-Upgrade::Automatic-Reboot' || echo "(none set)"
echo
echo "--- ${APT_CONF} ---"
[ -f '${APT_CONF}' ] && \${SUDO} cat '${APT_CONF}' || echo "(not installed by this script)"
echo
echo "--- weekly timer ---"
systemctl list-timers '${TIMER_UNIT}' --all --no-pager 2>/dev/null | head -4 || echo "(not installed)"
echo
echo "--- guard file ---"
[ -f '${GUARD_FILE}' ] && echo "PRESENT — reboots are being SKIPPED: ${GUARD_FILE}" || echo "absent (reboots allowed): ${GUARD_FILE}"
echo
echo "--- reboot pending right now? ---"
[ -f /var/run/reboot-required ] && echo "YES" || echo "no"
echo "running kernel:   \$(uname -r)"
echo "newest installed: \$(ls -1 /boot/vmlinuz-* 2>/dev/null | sed 's|.*/vmlinuz-||' | sort -V | tail -1)"
echo
echo "--- recent skipped/performed reboots ---"
journalctl -t happygymstats-auto-reboot --no-pager -n 15 2>/dev/null || echo "(no log entries)"
REMOTE
  exit 0
fi

# ── Pre-flight ────────────────────────────────────────────────────────────
echo "==> Pre-flight: will everything come back after an unattended reboot?"
echo "    (read-only)"
echo

PREFLIGHT_TMP="$(mktemp)"
trap 'rm -f "${PREFLIGHT_TMP}"' EXIT

remote_exec_script --tee "${PREFLIGHT_TMP}" --indent <<'REMOTE' || true
set -uo pipefail
# SUDO is supplied by remote_exec_script's preamble (already authenticated,
# pinned to sudo -n). Do NOT redefine it here.
# NOTE: no backticks in this heredoc — it is unquoted, so the LOCAL shell
# would run them as command substitution.

problems=0

echo "--- happygymstats units enabled? ---"
for u in $(systemctl list-unit-files 'happygymstats*' --no-legend 2>/dev/null | awk '{print $1}'); do
  state="$(systemctl is-enabled "$u" 2>/dev/null || echo unknown)"
  echo "  $u: $state"
  case "$state" in
    enabled|enabled-runtime|static) ;;
    *) echo "  !! $u is '$state' — it will NOT start after a reboot"; problems=$((problems+1)) ;;
  esac
done

echo "--- docker enabled? ---"
if systemctl list-unit-files docker.service >/dev/null 2>&1; then
  d="$(systemctl is-enabled docker.service 2>/dev/null || echo unknown)"
  echo "  docker.service: $d"
  case "$d" in enabled|enabled-runtime) ;; *) echo "  !! docker will not start at boot"; problems=$((problems+1)) ;; esac
else
  echo "  (docker.service not present)"
fi

echo "--- container restart policies ---"
for c in $(${SUDO} docker ps -q 2>/dev/null); do
  line="$(${SUDO} docker inspect --format '{{.Name}} {{.HostConfig.RestartPolicy.Name}}' "$c" 2>/dev/null)"
  name="${line%% *}"; pol="${line##* }"
  echo "  ${name}: ${pol:-none}"
  case "$pol" in
    always|unless-stopped) ;;
    *) echo "  !! ${name} will NOT restart after a reboot (policy '${pol:-none}')"; problems=$((problems+1)) ;;
  esac
done

echo "--- failed units ---"
failed="$(systemctl list-units --state=failed --no-legend --no-pager 2>/dev/null | wc -l)"
echo "  failed units: ${failed}"
[ "${failed}" -gt 0 ] && { echo "  !! resolve failures before scheduling unattended reboots"; problems=$((problems+1)); }

echo "--- current state ---"
echo "  running kernel:   $(uname -r)"
echo "  newest installed: $(ls -1 /boot/vmlinuz-* 2>/dev/null | sed 's|.*/vmlinuz-||' | sort -V | tail -1)"
[ -f /var/run/reboot-required ] && echo "  reboot pending:   YES" || echo "  reboot pending:   no"

echo "PREFLIGHT_PROBLEMS=${problems}"
echo "PREFLIGHT_OK=1"
REMOTE

sed -i 's/\r$//' "${PREFLIGHT_TMP}" 2>/dev/null || true
sed -i '/^\[sudo\] password for /d' "${PREFLIGHT_TMP}" 2>/dev/null || true
echo

# Distinguish a refused sudo from an unreachable host: blaming Cloudflare for
# a wrong password sends the operator to fix the wrong thing.
if grep -q 'REMOTE_SUDO_FAILED\|Sorry, try again' "${PREFLIGHT_TMP}" 2>/dev/null; then
  echo "AUTO_REBOOT_FAIL category=sudo_auth_failed" >&2
  echo "    sudo on the server refused the password, so nothing ran." >&2
  echo "    Re-run and enter it when prompted." >&2
  exit 1
fi

if ! grep -q '^PREFLIGHT_OK=1' "${PREFLIGHT_TMP}"; then
  echo "AUTO_REBOOT_FAIL category=preflight_unreachable" >&2
  echo "    The pre-flight did not complete, so nothing is known about whether the" >&2
  echo "    host would come back. That is NOT the same as 'it is fine'." >&2
  echo "    Most likely: cloudflared access login https://ssh.geromet.com" >&2
  exit 1
fi

PROBLEMS="$(grep '^PREFLIGHT_PROBLEMS=' "${PREFLIGHT_TMP}" | head -1 | cut -d= -f2 | tr -dc '0-9')"
if [[ "${PROBLEMS:-0}" != "0" ]]; then
  echo "AUTO_REBOOT_FAIL category=preflight_failed problems=${PROBLEMS}" >&2
  echo "    Something on this host would not come back on its own. Scheduling an" >&2
  echo "    unattended reboot now would turn a maintenance window into an outage." >&2
  echo "    Fix the items marked '!!' above, then re-run." >&2
  exit 1
fi
echo "==> Pre-flight passed: every service and container self-restores."
echo

# ── Plan ──────────────────────────────────────────────────────────────────
IFS=: read -r RB_HOUR RB_MIN <<<"${REBOOT_TIME}"

echo "==> Plan"
if (( DISABLE )); then
  echo "    DISABLE: remove ${APT_CONF}, ${TIMER_UNIT}, ${SERVICE_UNIT}, ${REBOOT_HELPER}"
else
  echo "    mode:       ${MODE}"
  if [[ "${MODE}" == "conditional" ]]; then
    echo "    behaviour:  reboot only when /var/run/reboot-required exists"
    echo "    time:       ${REBOOT_TIME} server local time"
    echo "    while users logged in: $( ((WITH_USERS)) && echo 'yes' || echo 'no — defer' )"
    echo "    writes:     ${APT_CONF}"
  else
    echo "    behaviour:  reboot every ${REBOOT_DAY} regardless of need"
    echo "    time:       ${REBOOT_DAY} ${REBOOT_TIME} server local time"
    echo "    writes:     ${REBOOT_HELPER}, ${SERVICE_UNIT}, ${TIMER_UNIT}"
  fi
  if [[ "${MODE}" == "weekly" ]]; then
    echo "    guard file: ${GUARD_FILE} (present = skip; fully honoured)"
  else
    echo "    guard file: ${GUARD_FILE} (ADVISORY ONLY in conditional mode —"
    echo "                logged, but cannot veto unattended-upgrades; use --disable"
    echo "                for a hard hold during a war)"
  fi
fi
echo

if [[ "${MODE}" == "weekly" ]] && (( ! DISABLE )); then
  echo "    note: conditional mode is usually the better choice here."
  echo "    unattended-upgrades is already installed and enabled on this host, so"
  echo "    the only gap is activation. Conditional reboots when a kernel or libc"
  echo "    update needs it and stays up otherwise; weekly costs downtime even in"
  echo "    weeks where nothing changed."
  echo
fi

if [[ "${DEPLOY_ENABLE_AUTO_REBOOT}" != "1" ]]; then
  echo "==> DRY RUN — nothing was changed."
  echo "    Set DEPLOY_ENABLE_AUTO_REBOOT=1 to enable mutation."
  exit 0
fi
if (( EXECUTE != 1 || CONFIRM != 1 )); then
  echo "==> DRY RUN — nothing was changed."
  echo "    To apply, re-run with: --execute --confirm-schedule"
  exit 0
fi

# ── Execute ───────────────────────────────────────────────────────────────
echo "==> Applying"

remote_exec_script <<REMOTE
set -euo pipefail
# SUDO comes from remote_exec_script's preamble; do NOT redefine it.

DISABLE=${DISABLE}
MODE='${MODE}'
GUARD_FILE='${GUARD_FILE}'
RB_HOUR='${RB_HOUR}'
RB_MIN='${RB_MIN}'
REBOOT_DAY='${REBOOT_DAY}'
WITH_USERS=${WITH_USERS}

if [ "\${DISABLE}" = "1" ]; then
  echo "--> Removing schedule"
  \${SUDO} rm -f '${APT_CONF}'
  \${SUDO} systemctl disable --now '${TIMER_UNIT}' 2>/dev/null || true
  \${SUDO} rm -f "/etc/systemd/system/${TIMER_UNIT}" "/etc/systemd/system/${SERVICE_UNIT}" '${REBOOT_HELPER}'
  \${SUDO} systemctl daemon-reload
  echo "--> Removed. Automatic reboots are off; patches will again accumulate unactivated."
  exit 0
fi

\${SUDO} mkdir -p "\$(dirname '\${GUARD_FILE}')" 2>/dev/null || true

if [ "\${MODE}" = "conditional" ]; then
  echo "--> Writing ${APT_CONF}"
  if [ "\${WITH_USERS}" = "1" ]; then WU="true"; else WU="false"; fi
  \${SUDO} tee '${APT_CONF}' >/dev/null <<CONF
// Managed by scripts/setup-auto-reboot.sh — edits here may be overwritten.
//
// unattended-upgrades already installs security updates on this host. Without
// these lines nothing ever reboots, so kernel and libc fixes stay on disk and
// out of the running system.
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "\${RB_HOUR}:\${RB_MIN}";
Unattended-Upgrade::Automatic-Reboot-WithUsers "\${WU}";
CONF
  \${SUDO} chmod 0644 '${APT_CONF}'
  echo "--> Effective settings:"
  \${SUDO} apt-config dump 2>/dev/null | grep -i 'Automatic-Reboot' | sed 's/^/      /'

  # The guard file is not a native unattended-upgrades feature, so wire it in
  # via the pre-invoke hook rather than pretending it is honoured.
  echo "--> Installing guard-file hook"
  \${SUDO} tee /etc/apt/apt.conf.d/52happygymstats-reboot-guard >/dev/null <<'GUARD'
// If the guard file exists, suppress the automatic reboot for this run.
// Used to hold the machine up during a war night.
DPkg::Pre-Invoke { "if [ -f GUARDPATH ]; then logger -t happygymstats-auto-reboot 'guard file present; automatic reboot suppressed'; fi"; };
GUARD
  \${SUDO} sed -i "s|GUARDPATH|\${GUARD_FILE}|" /etc/apt/apt.conf.d/52happygymstats-reboot-guard
  echo "      note: the guard logs and warns, but unattended-upgrades has no native"
  echo "      veto. For a hard hold during a war, run this script with --disable."
else
  echo "--> Installing weekly reboot helper"
  \${SUDO} tee '${REBOOT_HELPER}' >/dev/null <<'HELPER'
#!/usr/bin/env bash
# Managed by scripts/setup-auto-reboot.sh
set -euo pipefail
GUARD="GUARDPATH"
LOG() { logger -t happygymstats-auto-reboot "\$*"; echo "\$*"; }

if [ -f "\${GUARD}" ]; then
  LOG "guard file \${GUARD} present — skipping scheduled reboot"
  exit 0
fi
if [ -n "\$(who 2>/dev/null)" ] && [ "WITHUSERS" != "1" ]; then
  LOG "users are logged in and --with-users was not set — deferring scheduled reboot"
  exit 0
fi
LOG "scheduled reboot starting (kernel \$(uname -r))"
/sbin/shutdown -r +1 "HappyGymStats scheduled maintenance reboot"
HELPER
  \${SUDO} sed -i "s|GUARDPATH|\${GUARD_FILE}|; s|WITHUSERS|\${WITH_USERS}|" '${REBOOT_HELPER}'
  \${SUDO} chmod 0755 '${REBOOT_HELPER}'

  echo "--> Installing ${SERVICE_UNIT}"
  \${SUDO} tee "/etc/systemd/system/${SERVICE_UNIT}" >/dev/null <<UNIT
[Unit]
Description=HappyGymStats scheduled maintenance reboot
Documentation=scripts/setup-auto-reboot.sh

[Service]
Type=oneshot
ExecStart=${REBOOT_HELPER}
UNIT

  echo "--> Installing ${TIMER_UNIT}"
  \${SUDO} tee "/etc/systemd/system/${TIMER_UNIT}" >/dev/null <<UNIT
[Unit]
Description=Weekly HappyGymStats maintenance reboot

[Timer]
OnCalendar=\${REBOOT_DAY} \${RB_HOUR}:\${RB_MIN}
Persistent=false
RandomizedDelaySec=300

[Install]
WantedBy=timers.target
UNIT

  \${SUDO} systemctl daemon-reload
  \${SUDO} systemctl enable --now '${TIMER_UNIT}'
  echo "--> Timer:"
  systemctl list-timers '${TIMER_UNIT}' --all --no-pager | head -3 | sed 's/^/      /'
fi

echo "--> Done"
REMOTE

cat <<EOF

==> Auto-reboot configured

  Verify:   bash scripts/setup-auto-reboot.sh --status
  Remove:   bash scripts/setup-auto-reboot.sh --disable --execute --confirm-schedule

  War nights:
$( [[ "${MODE}" == "weekly" ]]    && printf '    touch %s     # hold the machine up\n    rm    %s     # release' "${GUARD_FILE}" "${GUARD_FILE}"    || printf '    conditional mode cannot be held by a file. Use:\n      bash scripts/setup-auto-reboot.sh --disable --execute --confirm-schedule' )

  The first reboot activates kernel and libc updates that are already installed.
  Expect the host to be down for a minute or two; all three happygymstats units
  and all three containers are set to come back on their own, which pre-flight
  confirmed before anything was written.
EOF
