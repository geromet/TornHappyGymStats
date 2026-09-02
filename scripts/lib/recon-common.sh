#!/usr/bin/env bash
# recon-common.sh — Shared reporting helpers for the read-only recon collectors.
#
# Not executed directly. scripts/recon-fetch.sh concatenates this file with a
# collector and pipes the result to `bash -s` over SSH, so the combined script
# never touches the remote filesystem.
#
# Two conventions the collectors depend on:
#
#   1. A section that could not be read prints "BLIND — needs root", never
#      "(empty)". Blank output reads as "nothing there", which is a wrong
#      answer rather than a missing one, and on a security report that
#      difference decides whether someone investigates.
#
#   2. Secrets are never printed. Where it matters whether two values are the
#      same, fingerprint() emits an HMAC under a random per-run key that is
#      never printed, so fingerprints are comparable within one report and
#      meaningless outside it.

HR="────────────────────────────────────────────────────────────"
section() { printf '\n%s\n  %s\n%s\n' "$HR" "$1" "$HR"; }
note()    { printf '  %s\n' "$1"; }
blank()   { printf '\n'; }

FINDINGS_FILE="$(mktemp 2>/dev/null || echo /tmp/recon-findings.$$)"
trap 'rm -f "${FINDINGS_FILE}" 2>/dev/null' EXIT

# Record something a human should look at. Severity is HIGH / MED / LOW / INFO.
finding() {
  printf '%s\t%s\n' "$1" "$2" >> "${FINDINGS_FILE}"
  printf '  [%s] %s\n' "$1" "$2"
}

# Replay every finding, highest severity first.
print_findings_summary() {
  section "Findings summary"
  if [[ ! -s "${FINDINGS_FILE}" ]]; then
    note "No findings recorded."
    return
  fi
  local sev count
  for sev in HIGH MED LOW INFO; do
    count="$(grep -c "^${sev}	" "${FINDINGS_FILE}" 2>/dev/null)"
    count="${count:-0}"
    (( count == 0 )) && continue
    printf '\n  %s (%s):\n' "${sev}" "${count}"
    grep "^${sev}	" "${FINDINGS_FILE}" | cut -f2- | sed 's/^/    - /'
  done
  local h m l i
  h="$(grep -c '^HIGH	' "${FINDINGS_FILE}" 2>/dev/null)"; h="${h:-0}"
  m="$(grep -c '^MED	'  "${FINDINGS_FILE}" 2>/dev/null)"; m="${m:-0}"
  l="$(grep -c '^LOW	'  "${FINDINGS_FILE}" 2>/dev/null)"; l="${l:-0}"
  i="$(grep -c '^INFO	' "${FINDINGS_FILE}" 2>/dev/null)"; i="${i:-0}"
  printf '\n  totals: HIGH=%s MED=%s LOW=%s INFO=%s\n' "$h" "$m" "$l" "$i"
}

# Privilege detection. ROOT_OK means privileged reads will succeed.
if [[ "$(id -u 2>/dev/null)" == "0" ]]; then
  SUDO=""
  ROOT_OK=1
  PRIV_MODE="running as root"
elif sudo -n true 2>/dev/null; then
  SUDO="sudo -n"
  ROOT_OK=1
  PRIV_MODE="passwordless sudo available"
else
  SUDO=""
  ROOT_OK=0
  PRIV_MODE="NO ROOT — privileged sections will report BLIND"
fi

# Marker for a section we genuinely could not read.
blind() {
  printf '    BLIND — needs root (%s)\n' "${1:-not readable as $(whoami 2>/dev/null || echo unknown)}"
}

# Run a command; indent output; never abort the survey on failure.
probe() {
  local label="$1"; shift
  printf '  %s:\n' "${label}"
  local output rc
  output="$("$@" 2>&1)"; rc=$?
  if [[ -z "${output}" ]]; then
    if (( rc != 0 )) && (( ROOT_OK == 0 )); then
      blind
    elif (( rc != 0 )); then
      printf '    (command failed, no output)\n'
    else
      printf '    (no output — nothing matched)\n'
    fi
    return
  fi
  printf '%s\n' "${output}" | sed 's/^/    /'
}

probe_sh() {
  local label="$1" snippet="$2"
  probe "${label}" bash -c "${snippet}"
}

# A probe that needs root: states BLIND explicitly rather than looking empty.
probe_priv() {
  local label="$1" snippet="$2"
  printf '  %s:\n' "${label}"
  if (( ROOT_OK == 0 )); then
    blind
    return
  fi
  local output
  output="$(bash -c "${SUDO} ${snippet}" 2>&1)"
  if [[ -z "${output}" ]]; then
    printf '    (no output — nothing matched)\n'
  else
    printf '%s\n' "${output}" | sed 's/^/    /'
  fi
}

# Distinguish absent / present / unreadable. `test -f` alone reports an
# unreadable-directory EACCES as "missing", which is a false negative that
# already produced one wrong line in an earlier report.
file_state() {
  local path="$1"
  if [[ -e "${path}" ]]; then
    printf 'present'
  elif (( ROOT_OK == 1 )) && bash -c "${SUDO} test -e '${path}'" 2>/dev/null; then
    printf 'present (only visible with root)'
  elif (( ROOT_OK == 0 )) && ! [[ -r "$(dirname "${path}")" ]]; then
    printf 'UNKNOWN — parent directory not readable without root'
  else
    printf 'absent'
  fi
}

# Per-run HMAC key for secret fingerprints. Random, never printed.
REPORT_FINGERPRINT_KEY="$(head -c 32 /dev/urandom 2>/dev/null | od -An -tx1 | tr -d ' \n')"
if [[ -z "${REPORT_FINGERPRINT_KEY}" ]]; then
  REPORT_FINGERPRINT_KEY="$$-$(date +%s%N 2>/dev/null)-${RANDOM}${RANDOM}"
fi

fingerprint() {
  local value="$1" out=""
  if command -v openssl >/dev/null 2>&1; then
    out="$(printf '%s' "${value}" \
      | openssl dgst -sha256 -hmac "${REPORT_FINGERPRINT_KEY}" 2>/dev/null \
      | awk '{print $NF}')"
  fi
  if [[ -z "${out}" ]] && command -v sha256sum >/dev/null 2>&1; then
    out="$(printf '%s%s' "${REPORT_FINGERPRINT_KEY}" "${value}" | sha256sum | cut -d' ' -f1)"
  fi
  [[ -z "${out}" ]] && { printf 'unavailable'; return; }
  printf '%s' "${out:0:12}"
}

report_header() {
  printf '%s\n' "$1"
  printf 'generated: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'host:      %s\n' "$(hostname 2>/dev/null || echo unknown)"
  printf 'user:      %s\n' "$(whoami 2>/dev/null || echo unknown)"
  printf 'privilege: %s\n' "${PRIV_MODE}"
  printf 'mutations: none (read-only collector)\n'
  if (( ROOT_OK == 0 )); then
    printf '\n'
    printf '  !! Running without root. Process ownership, firewall rules, sshd\n'
    printf '  !! config and file modes cannot be read, and those are most of the\n'
    printf '  !! answer. Re-run with root for a conclusive report:\n'
    printf '  !!   bash scripts/recon-fetch.sh <collector> --sudo\n'
  fi
}
