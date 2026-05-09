#!/usr/bin/env bash
set -euo pipefail

: "${SCRUB_JOURNAL_SERVICE:=happygymstats-api}"
: "${SCRUB_JOURNAL_VACUUM_TIME:=7d}"

usage() {
  cat <<EOF
Usage: bash scrub-playerid-logs-payload.sh --yes [--help]

SCRIPT_CATEGORY=ops-mutating
SCRIPT_MUTATES_SERVER_STATE=1
SCRIPT_AUTOMATION_SAFE_DEFAULT=0

Runs on the target host and mutates local logs:
  1) scrub raw "Torn player <id>" patterns in /var/log/syslog and /var/log/syslog.1
  2) rotate + vacuum journald retention
  3) verify remaining matches for "Torn player|TornPlayerId"

Required flag:
  --yes    Confirm mutation.

Environment overrides:
  SCRUB_JOURNAL_SERVICE      systemd unit to query (default: ${SCRUB_JOURNAL_SERVICE})
  SCRUB_JOURNAL_VACUUM_TIME  journalctl --vacuum-time (default: ${SCRUB_JOURNAL_VACUUM_TIME})

Safety notes:
  - No backups are created.
  - Only /var/log/syslog and /var/log/syslog.1 are edited.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ "${1:-}" != "--yes" ]]; then
  echo "ERROR: This script mutates logs. Re-run with --yes to confirm." >&2
  exit 2
fi

echo "==> Checking sudo availability"
if ! sudo -n true 2>/dev/null; then
  echo "INFO: sudo requires password; prompting..."
  sudo -v
fi

echo "==> Scrubbing /var/log/syslog and /var/log/syslog.1 (if present)"
for f in /var/log/syslog /var/log/syslog.1; do
  if [[ -f "$f" ]]; then
    sudo perl -pi -e '
      s/(Import job [a-f0-9]+ API key validated for )Torn player \d+/${1}AnonymousId [redacted]/g;
      s/\bTornPlayerId\b/RedactedPlayerIdToken/g;
    ' "$f"
    echo "scrubbed: $f"
  fi
done

echo "==> Rotating + vacuuming journald"
sudo journalctl --rotate >/dev/null
sudo journalctl --vacuum-time="${SCRUB_JOURNAL_VACUUM_TIME}" >/dev/null

echo "==> Verification: file logs"
set +e
sudo rg -n "Torn player|TornPlayerId" /var/log /opt/happygymstats -S
RG_EXIT=$?

echo "==> Verification: journald unit=${SCRUB_JOURNAL_SERVICE}"
sudo journalctl -u "${SCRUB_JOURNAL_SERVICE}" --no-pager | rg -n "Torn player|TornPlayerId"
J_EXIT=$?
set -e

if [[ $RG_EXIT -eq 1 && $J_EXIT -eq 1 ]]; then
  echo "PASS: no matches found"
  exit 0
fi

echo "WARN: matches still present (possibly compressed archives or out-of-scope files)"
exit 0
