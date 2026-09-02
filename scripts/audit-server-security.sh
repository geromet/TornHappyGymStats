#!/usr/bin/env bash
# audit-server-security.sh — System health and security review.
#
# SCRIPT_CATEGORY=audit
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Read-only. Requires scripts/lib/recon-common.sh prepended (recon-fetch.sh does this).
#
# Scoped to "what does a hurried, agent-assisted setup miss", not a full CIS
# benchmark. That keeps it finishable and matches the actual question. The
# emphasis is on things that get left behind: a scheduled job nobody removed, a
# container image never re-pulled, a secret file readable by the wrong account,
# a hardening default silently reverted.
#
# Secret discipline is the same as the other collectors: key fingerprints and
# counts, never key material; file modes and owners, never contents. No shadow
# file, no env file, no private key is ever read.
set -uo pipefail

report_header "Server security and health audit"

# ─────────────────────────────────────────────────────────────
section "Patch state"
probe_sh "OS" "grep -E '^(NAME|VERSION)=' /etc/os-release 2>/dev/null"
probe_sh "kernel" "uname -r"
blank
RUNNING_KERNEL="$(uname -r)"
NEWEST_KERNEL="$(ls -1 /boot/vmlinuz-* 2>/dev/null | sed 's|.*/vmlinuz-||' | sort -V | tail -1)"
note "running kernel:  ${RUNNING_KERNEL}"
note "newest installed: ${NEWEST_KERNEL:-unknown}"
if [[ -f /var/run/reboot-required ]]; then
  if [[ -n "${NEWEST_KERNEL}" && "${NEWEST_KERNEL}" != "${RUNNING_KERNEL}" ]]; then
    run_abi="$(printf '%s' "${RUNNING_KERNEL}" | grep -oE '^[0-9]+\.[0-9]+\.[0-9]+-[0-9]+' || echo "${RUNNING_KERNEL}")"
    new_abi="$(printf '%s' "${NEWEST_KERNEL}" | grep -oE '^[0-9]+\.[0-9]+\.[0-9]+-[0-9]+' || echo "${NEWEST_KERNEL}")"
    finding HIGH "running kernel ${run_abi} but ${new_abi} is installed — the patched kernel is on disk and NOT in use, so every kernel CVE fixed between them is live until reboot"
  else
    finding MED "reboot required to activate installed library updates"
  fi
  probe_sh "reboot-required packages" "cat /var/run/reboot-required.pkgs 2>/dev/null || echo '(list unavailable)'"
else
  note "no pending reboot flagged"
fi
blank
probe_priv "pending security updates" \
  "apt-get -s upgrade 2>/dev/null | grep -ciE '^Inst .*security' | xargs -I{} echo '{} security updates pending' || echo 'apt simulation unavailable'"
blank
probe_sh "unattended-upgrades installed?" \
  "dpkg -l unattended-upgrades 2>/dev/null | grep -q '^ii' && echo 'installed' || echo 'NOT INSTALLED'"
probe_sh "unattended-upgrades enabled?" \
  "grep -hsE 'Update-Package-Lists|Unattended-Upgrade' /etc/apt/apt.conf.d/20auto-upgrades 2>/dev/null || echo '(20auto-upgrades not present)'"

if command -v dpkg >/dev/null 2>&1; then
  if ! dpkg -l unattended-upgrades 2>/dev/null | grep -q '^ii'; then
    finding HIGH "unattended-upgrades is not installed — security patches are not applied automatically"
  elif ! grep -qs '"1"' /etc/apt/apt.conf.d/20auto-upgrades 2>/dev/null; then
    finding MED "unattended-upgrades is installed but may not be enabled — check 20auto-upgrades"
  fi
else
  note "not a dpkg-based system; automatic-update check skipped"
fi

# ─────────────────────────────────────────────────────────────
section "SSH configuration"
note "This host binds :22 to loopback only and is reached through a cloudflared"
note "tunnel, which is a strong posture. These checks confirm it has not drifted."
blank
probe_priv "effective sshd config (key directives)" \
  "sshd -T 2>/dev/null | grep -iE '^(passwordauthentication|permitrootlogin|pubkeyauthentication|listenaddress|port|permitemptypasswords|x11forwarding|allowtcpforwarding|kbdinteractiveauthentication|challengeresponseauthentication) ' || grep -hiE '^\\s*(PasswordAuthentication|PermitRootLogin|PubkeyAuthentication|ListenAddress|Port)' /etc/ssh/sshd_config /etc/ssh/sshd_config.d/*.conf 2>/dev/null"

if (( ROOT_OK == 1 )); then
  sshd_dump="$(bash -c "${SUDO} sshd -T 2>/dev/null" || true)"
  if [[ -n "${sshd_dump}" ]]; then
    echo "${sshd_dump}" | grep -qi '^passwordauthentication yes' \
      && finding HIGH "sshd permits password authentication — key-only is expected here"
    echo "${sshd_dump}" | grep -qiE '^permitrootlogin (yes|without-password)' \
      && finding MED "sshd permits root login ($(echo "${sshd_dump}" | grep -i '^permitrootlogin'))"
    echo "${sshd_dump}" | grep -qi '^permitemptypasswords yes' \
      && finding HIGH "sshd permits empty passwords"
  fi
fi

blank
probe_sh "sshd_config.d drop-ins" "ls -l /etc/ssh/sshd_config.d/ 2>/dev/null || echo '(none)'"

# ─────────────────────────────────────────────────────────────
section "Accounts and keys"
note "Key fingerprints and counts only — no key material is printed."
blank
probe_sh "accounts with a login shell" \
  "awk -F: '\$7 !~ /(nologin|false|sync)\$/ {printf \"  %s uid=%s shell=%s\\n\", \$1, \$3, \$7}' /etc/passwd 2>/dev/null"
blank
probe_sh "sudo/admin group members" \
  "getent group sudo admin wheel 2>/dev/null | awk -F: '{printf \"  %s: %s\\n\", \$1, \$4}'"
blank
probe_sh "UID 0 accounts (should be root only)" \
  "awk -F: '\$3==0 {print \"  \"\$1}' /etc/passwd 2>/dev/null"

uid0_count="$(awk -F: '$3==0' /etc/passwd 2>/dev/null | wc -l)"
(( uid0_count > 1 )) && finding HIGH "more than one UID 0 account exists (${uid0_count})"

blank
printf '  authorized_keys inventory:\n'
for home in /root /home/*; do
  ak="${home}/.ssh/authorized_keys"
  state="$(file_state "${ak}")"
  if [[ "${state}" == present* ]]; then
    if (( ROOT_OK == 1 )); then
      count="$(bash -c "${SUDO} grep -cvE '^\\s*(#|$)' '${ak}' 2>/dev/null" || echo '?')"
      printf '    %s: %s key(s)\n' "${ak}" "${count}"
      bash -c "${SUDO} ssh-keygen -lf '${ak}' 2>/dev/null" | sed 's/^/      /' || printf '      (fingerprints unavailable)\n'
    else
      count="$(grep -cvE '^\s*(#|$)' "${ak}" 2>/dev/null || echo '?')"
      printf '    %s: %s key(s)\n' "${ak}" "${count}"
      ssh-keygen -lf "${ak}" 2>/dev/null | sed 's/^/      /' || printf '      (not readable)\n'
    fi
  else
    printf '    %s: %s\n' "${ak}" "${state}"
  fi
done
blank
probe_sh "recent successful logins" "last -n 15 2>/dev/null | head -18 || echo '(wtmp unavailable)'"
blank
probe_priv "recent failed auth attempts (count by source)" \
  "journalctl -u ssh -u sshd --since '7 days ago' 2>/dev/null | grep -v 'sudo\\[' | grep -ci 'authentication failure\\|Failed password' | xargs -I{} echo '{} failed auth events in the last 7 days'"

# ─────────────────────────────────────────────────────────────
section "Intrusion prevention"
probe_sh "fail2ban" \
  "dpkg -l fail2ban 2>/dev/null | grep -q '^ii' && (systemctl is-active fail2ban 2>/dev/null) || echo 'NOT INSTALLED'"
dpkg -l fail2ban 2>/dev/null | grep -q '^ii' || \
  finding LOW "fail2ban not installed — low impact here because :22 is loopback-only, but public :25/:80/:443 still see traffic"

# ─────────────────────────────────────────────────────────────
section "Scheduled jobs — what was left behind"
note "High value on a host that agents had shell access to: a forgotten timer or"
note "cron entry is how a one-off fix becomes permanent unreviewed behaviour."
blank
probe_priv "root crontab" "crontab -l -u root 2>/dev/null || echo '(no root crontab)'"
blank
probe_sh "user crontabs present" \
  "ls -l /var/spool/cron/crontabs/ 2>/dev/null || echo '(none or not readable)'"
blank
probe_sh "/etc/cron.d entries" "ls -l /etc/cron.d/ 2>/dev/null"
blank
probe_sh "cron.{hourly,daily,weekly,monthly}" \
  "for d in hourly daily weekly monthly; do echo \"[cron.\$d]\"; ls /etc/cron.\$d/ 2>/dev/null | sed 's/^/  /'; done"
blank
probe_sh "enabled systemd timers" \
  "systemctl list-timers --all --no-pager 2>/dev/null | head -25"
blank
probe_sh "non-vendor systemd units (locally added)" \
  "ls -l /etc/systemd/system/*.service 2>/dev/null | awk '{print \$NF, \$6, \$7, \$8}' || echo '(none)'"

# ─────────────────────────────────────────────────────────────
section "Secret file permissions"
note "Modes and owners only. Contents are never read."
blank
# Only genuine secrets belong here. A certificate is public by design — it is
# handed to every TLS client — so /etc/ssl/cloudflare/origin.pem is reported
# below for completeness but never flagged. Only the private key and the env
# files are secrets.
for f in /etc/happygymstats/api.env /etc/happygymstats/api-dev.env \
         /etc/ssl/cloudflare/origin.key; do
  state="$(file_state "${f}")"
  if [[ "${state}" == present* ]]; then
    if (( ROOT_OK == 1 )); then
      line="$(bash -c "${SUDO} stat -c '%a %U:%G %n' '${f}' 2>/dev/null")"
      printf '    %s\n' "${line:-${f}: stat failed}"
      mode="${line%% *}"
      # For a secret, ANY permission for "other" is wrong. The last octal digit
      # is the other-class: 0 is the only acceptable value. (An earlier version
      # tested [2367] and so missed 644 — world-readable — which is the very
      # case this check exists for.)
      other="${mode: -1}"
      group="${mode: -2:1}"
      if [[ "${other}" != "0" ]]; then
        if [[ "${other}" == *[2367]* ]]; then
          finding HIGH "${f} is world-WRITABLE (mode ${mode})"
        else
          finding HIGH "${f} is world-readable (mode ${mode}) — secrets should be 0640 or tighter"
        fi
      elif [[ "${group}" == *[2367]* ]]; then
        finding LOW "${f} is group-writable (mode ${mode}) — confirm the group is trusted"
      fi
    else
      printf '    %s: %s (mode needs root)\n' "${f}" "${state}"
    fi
  else
    printf '    %s: %s\n' "${f}" "${state}"
  fi
done
blank
note "Public certificate (not a secret; world-readable is correct):"
probe_priv "origin.pem" "stat -c '%a %U:%G %n' /etc/ssl/cloudflare/origin.pem 2>/dev/null"
blank
probe_priv "/etc/happygymstats directory" "stat -c '%a %U:%G %n' /etc/happygymstats 2>/dev/null"
probe_priv "/etc/ssl/cloudflare directory" "stat -c '%a %U:%G %n' /etc/ssl/cloudflare 2>/dev/null"

# ─────────────────────────────────────────────────────────────
section "TLS certificate validity"
probe_priv "origin certificate" \
  "openssl x509 -in /etc/ssl/cloudflare/origin.pem -noout -subject -ext subjectAltName -dates 2>/dev/null"

if (( ROOT_OK == 1 )); then
  enddate="$(bash -c "${SUDO} openssl x509 -in /etc/ssl/cloudflare/origin.pem -noout -enddate 2>/dev/null" | cut -d= -f2)"
  if [[ -n "${enddate}" ]]; then
    exp_epoch="$(date -d "${enddate}" +%s 2>/dev/null || echo 0)"
    now_epoch="$(date +%s)"
    if (( exp_epoch > 0 )); then
      days=$(( (exp_epoch - now_epoch) / 86400 ))
      note "origin certificate expires in ${days} day(s)"
      (( days < 30 )) && finding HIGH "origin certificate expires in ${days} days"
      (( days >= 30 && days < 90 )) && finding MED "origin certificate expires in ${days} days"
    fi
  fi
fi

# ─────────────────────────────────────────────────────────────
section "Container posture"
note "Containers 'Up 3 months' have not been re-pulled in 3 months. Every CVE"
note "patched upstream since then is unapplied, however healthy they look."
blank
probe_sh "containers with uptime and image" \
  "${SUDO} docker ps --format '{{.Names}}\t{{.Status}}\t{{.Image}}' 2>/dev/null || docker ps --format '{{.Names}}\t{{.Status}}\t{{.Image}}' 2>/dev/null || echo 'docker not queryable'"
blank
probe_priv "image creation dates (how old is the running image)" \
  "docker images --format '{{.Repository}}:{{.Tag}}\t{{.CreatedSince}}\t{{.Size}}' 2>/dev/null"
blank
probe_priv "privileged or host-network containers" \
  "docker ps -q 2>/dev/null | xargs -r docker inspect --format '{{.Name}} privileged={{.HostConfig.Privileged}} network={{.HostConfig.NetworkMode}} user={{.Config.User}}' 2>/dev/null"
blank
probe_priv "restart policies" \
  "docker ps -q 2>/dev/null | xargs -r docker inspect --format '{{.Name}} restart={{.HostConfig.RestartPolicy.Name}}' 2>/dev/null"

if (( ROOT_OK == 1 )); then
  while read -r line; do
    [[ -z "${line}" ]] && continue
    name="$(echo "${line}" | awk '{print $1}')"
    if echo "${line}" | grep -q 'privileged=true'; then
      finding HIGH "container ${name} runs privileged"
    fi
    if echo "${line}" | grep -q 'user=$\|user= '; then
      : # empty user means image default, often root — reported below rather than per-container
    fi
  done < <(bash -c "${SUDO} docker ps -q 2>/dev/null" | xargs -r -I{} bash -c "${SUDO} docker inspect --format '{{.Name}} privileged={{.HostConfig.Privileged}} user={{.Config.User}}' {} 2>/dev/null")

  # grep -c prints a count and exits non-zero on no match; a `|| echo 0`
  # fallback would append a SECOND zero and break the arithmetic below.
  old_containers="$(bash -c "${SUDO} docker ps --format '{{.Names}}\t{{.Status}}' 2>/dev/null" | grep -ciE 'Up [0-9]+ (months|years)')"
  old_containers="${old_containers:-0}"
  (( old_containers > 0 )) && finding MED "${old_containers} container(s) running for months without an image refresh — unpatched CVEs accumulate"
fi

# ─────────────────────────────────────────────────────────────
section "Keycloak version and CVE posture"
note "Keycloak fronts every authorization decision in this stack — the admin-only"
note "dev host and AdminPanel are both only as strong as it is. Version and the"
note "reset-credentials setting are checked here so they are not left to memory."
blank
probe_sh "running image" \
  "${SUDO} docker ps --format '{{.Names}}\t{{.Image}}' 2>/dev/null | grep -i keycloak || docker ps --format '{{.Names}}\t{{.Image}}' 2>/dev/null | grep -i keycloak || echo '(no keycloak container visible)'"
blank
probe_priv "image digest and creation date" \
  "docker ps --filter 'name=keycloak' --format '{{.Image}}' 2>/dev/null | head -1 | xargs -r docker image inspect --format '{{.Id}} created={{.Created}}' 2>/dev/null"

KC_IMAGE="$(bash -c "${SUDO} docker ps --format '{{.Image}}' 2>/dev/null" 2>/dev/null | grep -i keycloak | head -1)"
[[ -z "${KC_IMAGE}" ]] && KC_IMAGE="$(docker ps --format '{{.Image}}' 2>/dev/null | grep -i keycloak | head -1)"

if [[ -n "${KC_IMAGE}" ]]; then
  KC_TAG="${KC_IMAGE##*:}"
  note "detected Keycloak tag: ${KC_TAG}"
  KC_MAJOR="${KC_TAG%%.*}"
  KC_REST="${KC_TAG#*.}"
  KC_MINOR="${KC_REST%%.*}"
  if [[ "${KC_MAJOR}" =~ ^[0-9]+$ && "${KC_MINOR}" =~ ^[0-9]+$ ]]; then
    # CVE-2026-18963 (CVSS 9.1, unauthenticated account takeover via the
    # reset-credentials flow) is fixed upstream in 26.7.2. Anything older on the
    # 26.x line is affected, and 26.0 is additionally end-of-life.
    # Fix line is exactly 26.7.2, so 26.7.0 and 26.7.1 are still affected.
    KC_PATCH="${KC_TAG##*.}"
    [[ "${KC_PATCH}" == "${KC_TAG}" || ! "${KC_PATCH}" =~ ^[0-9]+$ ]] && KC_PATCH=0
    if (( KC_MAJOR < 26 )) \
       || { (( KC_MAJOR == 26 )) && (( KC_MINOR < 7 )); } \
       || { (( KC_MAJOR == 26 )) && (( KC_MINOR == 7 )) && (( KC_PATCH < 2 )); }; then
      finding HIGH "Keycloak ${KC_TAG} predates 26.7.2 — affected by CVE-2026-18963 (CVSS 9.1, unauthenticated account takeover). Exploitation requires the realm's Forgot-password flow to be enabled; see the reset-credentials probe below."
      if (( KC_MAJOR == 26 )) && (( KC_MINOR == 0 )); then
        finding HIGH "Keycloak 26.0 is end-of-life and receives no security updates at all — upgrade to a supported 26.7.x"
      fi
    else
      note "Keycloak ${KC_TAG} is at or beyond the CVE-2026-18963 fix line (26.7.2)"
    fi
  else
    finding LOW "could not parse Keycloak version from tag '${KC_TAG}' — check it by hand against 26.7.2"
  fi
else
  note "no Keycloak container detected; version checks skipped"
fi

blank
note "Reset-credentials (Forgot password) — the precondition for CVE-2026-18963."
note "Probed over loopback only; this never leaves the host."
# The earlier version built an auth URL with client_id=account and a guessed
# redirect_uri; Keycloak rejects the mismatched redirect before rendering a
# login page, so it returned nothing and the report said "realm may not exist"
# about a realm that plainly does. Confirm the realm first, then probe the
# reset-credentials endpoint directly — it returns a page when the flow is on
# and an error when it is off, without needing a client at all.
probe_sh "reset-credentials flow, per realm" \
  "for realm in torn master; do
     disco=\"\$(curl -fsS --max-time 8 \"http://127.0.0.1:8080/realms/\${realm}/.well-known/openid-configuration\" 2>/dev/null)\"
     if [ -z \"\$disco\" ]; then
       echo \"  \${realm}: realm not reachable on loopback — skipped (not evidence of anything)\"
       continue
     fi
     code=\"\$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 \"http://127.0.0.1:8080/realms/\${realm}/login-actions/reset-credentials\" 2>/dev/null)\"
     case \"\$code\" in
       200) echo \"  \${realm}: realm OK; reset-credentials returned 200 — FORGOT-PASSWORD LIKELY ENABLED\" ;;
       400|403|404) echo \"  \${realm}: realm OK; reset-credentials returned \${code} — flow appears disabled\" ;;
       *)   echo \"  \${realm}: realm OK; reset-credentials returned \${code} — INCONCLUSIVE, confirm in the console\" ;;
     esac
   done"
blank
note "A disabled Forgot-password flow removes the CVE-2026-18963 attack path, and"
note "admin 2FA blunts credential-based takeover generally. Neither is a substitute"
note "for upgrading: 26.0 is EOL, and 26.7.2 also fixes CVE-2026-17048 (admin API"
note "vault secret leak) and CVE-2026-14613 (fine-grained admin permission bypass),"
note "which do not depend on the reset flow."
blank
note "Not checkable without admin credentials, so verify these in the console:"
note "  - clients with a wildcard '*' in Valid Redirect URIs (CVE-2026-7504)"
note "  - required actions / OTP policy on admin accounts"
note "  - which accounts hold realm-admin"

# ─────────────────────────────────────────────────────────────
section "PostgreSQL version"
probe_sh "running image" \
  "${SUDO} docker ps --format '{{.Names}}\t{{.Image}}' 2>/dev/null | grep -i postgres || docker ps --format '{{.Names}}\t{{.Image}}' 2>/dev/null | grep -i postgres || echo '(no postgres container visible)'"
blank
probe_priv "server version" \
  "docker ps --format '{{.Names}}' 2>/dev/null | grep -i postgres | head -1 | xargs -r -I{} docker exec {} postgres --version 2>/dev/null"
blank
note "PostgreSQL 16.15 (2026-08-13) fixes CVE-2026-14664 / 14669 (heap overflows"
note "reaching arbitrary code as the postgres OS user), CVE-2026-14663 (pgcrypto"
note "silently using cleartext for disabled ciphers) and CVE-2026-6473. All require"
note "an authenticated database user or query authorship, and this server is bound"
note "to loopback with no published port — escalation, not entry."

PG_VER="$(bash -c "${SUDO} docker ps --format '{{.Names}}' 2>/dev/null" 2>/dev/null | grep -i postgres | head -1 | xargs -r -I{} bash -c "${SUDO} docker exec {} postgres --version 2>/dev/null" | grep -oE '[0-9]+\.[0-9]+' | head -1)"
if [[ -n "${PG_VER}" ]]; then
  PG_MAJ="${PG_VER%%.*}"; PG_MIN="${PG_VER#*.}"
  note "detected PostgreSQL ${PG_VER}"
  if [[ "${PG_MAJ}" == "16" ]] && [[ "${PG_MIN}" =~ ^[0-9]+$ ]] && (( PG_MIN < 15 )); then
    finding MED "PostgreSQL ${PG_VER} predates 16.15 — patched CVEs are reachable only by an authenticated DB user, but this database holds the faction data"
  fi
fi

# ─────────────────────────────────────────────────────────────
section "nginx exposure surface"
probe_sh "enabled sites" "ls -l /etc/nginx/sites-enabled/ 2>/dev/null"
blank
if [[ -e /etc/nginx/sites-enabled/default ]]; then
  finding LOW "the stock nginx 'default' site is still enabled — it answers for any unmatched hostname; remove it or replace with a catch-all that returns 444"
fi
probe_priv "server_name inventory" \
  "nginx -T 2>/dev/null | grep -E '^\\s*server_name' | sort -u"
blank
probe_priv "nginx config test" "nginx -t 2>&1"
blank
# nginx accepts duplicate server_name and silently serves the FIRST matching
# block, so a duplicate is a routing bug that never announces itself.
if (( ROOT_OK == 1 )); then
  # tr on spaces alone leaves tabs behind, which then count as a "duplicate".
  dupes="$(bash -c "${SUDO} nginx -T 2>/dev/null" \
    | grep -oE '^[[:space:]]*server_name[^;]*;' \
    | sed 's/[[:space:]]*server_name[[:space:]]*//; s/;//' \
    | tr '[:space:]' '\n' \
    | grep -vE '^$|^_$' \
    | sort | uniq -d)"
  if [[ -n "${dupes}" ]]; then
    finding MED "duplicate nginx server_name(s) — nginx serves the first matching block and ignores the rest: $(echo "${dupes}" | tr '\n' ' ')"
  else
    note "no duplicate server_name entries"
  fi
fi

# ─────────────────────────────────────────────────────────────
section "Service health"
probe_sh "failed systemd units" \
  "systemctl list-units --state=failed --no-pager 2>/dev/null | head -20"
failed_count="$(systemctl list-units --state=failed --no-legend --no-pager 2>/dev/null | wc -l)"
(( failed_count > 0 )) && finding MED "${failed_count} systemd unit(s) in failed state"
blank
probe_sh "happygymstats units" "systemctl list-units 'happygymstats*' --all --no-pager 2>/dev/null"
blank
probe_priv "recent unit crashes/restarts (7d)" \
  "journalctl --since '7 days ago' -p err --no-pager 2>/dev/null | grep -v 'sudo\\[' | grep -iE 'happygymstats|nginx|postgres|keycloak' | tail -20"

# ─────────────────────────────────────────────────────────────
section "Memory, swap and OOM history"
probe_sh "memory" "free -h 2>/dev/null"
blank
swap_total="$(free -m 2>/dev/null | awk '/^Swap:/{print $2}')"
if [[ "${swap_total}" == "0" ]]; then
  finding MED "no swap configured — memory pressure produces OOM kills rather than slowdown; adding two more .NET services will tighten this"
fi
probe_priv "past OOM kills" \
  "journalctl -k --no-pager 2>/dev/null | grep -v 'sudo\\[' | grep -i 'out of memory\\|oom-killer\\|Killed process' | tail -10 || echo '(none found)'"
blank
probe_sh "top memory consumers" \
  "ps -eo pid,rss,comm --sort=-rss 2>/dev/null | head -12 | awk '{printf \"  %-8s %8s KB  %s\\n\", \$1, \$2, \$3}'"

# ─────────────────────────────────────────────────────────────
section "Disk"
probe_sh "filesystem usage" "df -h 2>/dev/null | grep -vE '^(tmpfs|udev|overlay)'"
blank
probe_sh "inode usage" "df -i 2>/dev/null | grep -vE '^(tmpfs|udev|overlay)'"
blank
root_use="$(df --output=pcent / 2>/dev/null | tail -1 | tr -dc '0-9')"
if [[ -n "${root_use}" ]]; then
  (( root_use > 85 )) && finding HIGH "root filesystem ${root_use}% full"
  (( root_use > 70 && root_use <= 85 )) && finding LOW "root filesystem ${root_use}% full"
fi
probe_sh "largest directories under /var" \
  "${SUDO} du -xh --max-depth=1 /var 2>/dev/null | sort -rh | head -8 || du -xh --max-depth=1 /var 2>/dev/null | sort -rh | head -8"

# ─────────────────────────────────────────────────────────────
section "Filesystem hygiene"
probe_priv "world-writable files outside /tmp and /proc" \
  "find / -xdev -type f -perm -0002 -not -path '/tmp/*' -not -path '/var/tmp/*' -not -path '/proc/*' -printf '%M %u %p\\n' 2>/dev/null | head -20 || echo '(none found)'"
blank
probe_priv "SUID binaries (review anything non-standard)" \
  "find / -xdev -type f -perm -4000 -printf '%M %u %p\\n' 2>/dev/null | sort -k3 | head -30"
blank
probe_sh "recently modified files in /etc (14d)" \
  "find /etc -xdev -type f -mtime -14 -printf '%TY-%Tm-%Td %p\\n' 2>/dev/null | sort -r | head -25 || echo '(none or not readable)'"

# ─────────────────────────────────────────────────────────────
section "Automated assessment"
note "Findings are recorded inline above and collected here."

print_findings_summary

section "Not covered by this audit"
note "- Hetzner Cloud Firewall: outside the VM, invisible from inside. Check the console."
note "- Cloudflare WAF/Access policies: check the Cloudflare dashboard."
note "- Keycloak realm config (clients, redirect URIs, group membership): needs"
note "  admin credentials and is deliberately not collected here."
note "- Application-level authorization logic: covered by the test suite, not by this."
note "- Anything requiring package-integrity verification (debsums) if not installed."

section "End"
note "Report complete. Nothing on this host was modified."
