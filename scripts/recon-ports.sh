#!/usr/bin/env bash
# recon-ports.sh — What is listening, who owns it, and what can actually reach it.
#
# SCRIPT_CATEGORY=recon
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Read-only. Requires scripts/lib/recon-common.sh prepended (recon-fetch.sh does this).
#
# `ss` tells you a socket binds 0.0.0.0. It does not tell you whether the
# internet can reach it — that is decided by layers ss cannot see:
#
#   * Docker publishes ports by inserting its own iptables rules, which are
#     evaluated BEFORE ufw's. A container published with -p 30033:30033 is
#     reachable from the internet even while `ufw status` reports the port
#     denied. This is the single most common way a host ends up more exposed
#     than its firewall claims, so it is checked explicitly below.
#   * A cloud firewall (Hetzner, in this case) sits outside the VM entirely and
#     is invisible from inside it. Nothing here can see it — noted where relevant
#     so this report cannot overstate exposure.
set -uo pipefail

report_header "Listening-port investigation"

section "Listening sockets with owning process"
# The process column is the whole point and needs root; without it ports cannot
# be attributed and this report answers nothing.
probe_priv "ss -ltnup (TCP+UDP, with process)" "ss -ltnup 2>/dev/null"
blank
probe_sh "ss -ltn (unprivileged fallback, no process column)" "ss -ltn 2>/dev/null"

section "Public vs loopback classification"
note "A socket on 127.0.0.1 cannot be reached from another machine."
note "A socket on 0.0.0.0 or [::] is reachable unless a firewall stops it."
blank
probe_sh "publicly bound TCP sockets" \
  "ss -ltn 2>/dev/null | awk 'NR>1 {print \$4}' | grep -E '^(0\\.0\\.0\\.0|\\[::\\]|\\*):' | sort -u || echo '(none)'"
blank
probe_sh "loopback-only TCP sockets" \
  "ss -ltn 2>/dev/null | awk 'NR>1 {print \$4}' | grep -E '^(127\\.|\\[::1\\])' | sort -u || echo '(none)'"

section "Attribution — which unit or container owns each port"
for port in 25 80 443 5047 5048 5182 5432 8080 20241 30033; do
  printf '  port %s:\n' "${port}"
  if ! ss -ltn 2>/dev/null | grep -q ":${port} "; then
    printf '    not listening\n'
    continue
  fi
  if (( ROOT_OK == 1 )); then
    owner="$(bash -c "${SUDO} ss -ltnp 2>/dev/null" | grep ":${port} " | sed 's/.*users:(//' | head -1)"
    printf '    process: %s\n' "${owner:-unattributed}"
    unit="$(bash -c "${SUDO} ss -ltnp 2>/dev/null" | grep ":${port} " | grep -oE 'pid=[0-9]+' | head -1 | cut -d= -f2)"
    if [[ -n "${unit}" ]]; then
      printf '    systemd unit: %s\n' "$(bash -c "${SUDO} ps -o unit= -p ${unit} 2>/dev/null" | tr -d ' ' || echo unknown)"
      printf '    cmdline: %s\n' "$(bash -c "${SUDO} tr '\\0' ' ' < /proc/${unit}/cmdline 2>/dev/null" | cut -c1-160 || echo unknown)"
    fi
  else
    blind "process attribution"
  fi
  bind="$(ss -ltn 2>/dev/null | grep ":${port} " | awk '{print $4}' | tr '\n' ' ')"
  printf '    bound on: %s\n' "${bind}"
done

section "Docker published ports — the ufw-bypass check"
note "A '0.0.0.0:PORT->' mapping here means Docker inserted iptables rules that"
note "are evaluated before ufw. Such a port is internet-reachable regardless of"
note "what 'ufw status' says about it."
blank
probe_sh "docker ps ports" \
  "${SUDO} docker ps --format '{{.Names}}\t{{.Ports}}' 2>/dev/null || docker ps --format '{{.Names}}\t{{.Ports}}' 2>/dev/null || echo 'docker not queryable as this user'"
blank
probe_priv "containers bound to all interfaces" \
  "docker ps --format '{{.Names}}\t{{.Ports}}' 2>/dev/null | grep -E '0\\.0\\.0\\.0:|\\[::\\]:' || echo '(none publish on all interfaces)'"

section "Firewall — ufw"
probe_priv "ufw status verbose" "ufw status verbose 2>/dev/null || echo 'ufw not installed'"

section "Firewall — iptables / nftables actual rules"
note "ufw is a front-end. These are the rules the kernel really evaluates."
blank
probe_priv "DOCKER-USER chain (where a Docker bypass would be blocked)" \
  "iptables -S DOCKER-USER 2>/dev/null || echo 'no DOCKER-USER chain'"
blank
probe_priv "DOCKER nat chain (published container ports)" \
  "iptables -t nat -S DOCKER 2>/dev/null || echo 'no DOCKER nat chain'"
blank
probe_priv "filter INPUT policy and rules" "iptables -S INPUT 2>/dev/null"
blank
probe_priv "nftables ruleset (summary)" \
  "nft list ruleset 2>/dev/null | head -80 || echo 'nft not present or empty'"

section "Port 25 — SMTP posture"
note "Ubuntu ships Postfix bound to loopback only. A :25 socket on all"
note "interfaces means that default was changed. A world-reachable :25 with a"
note "permissive mynetworks or relay policy is an open relay."
blank
probe_sh "what is on 25" "ss -ltn 2>/dev/null | grep ':25 ' || echo 'not listening'"
blank
probe_priv "postfix effective config (key directives)" \
  "postconf -n 2>/dev/null | grep -E 'inet_interfaces|mynetworks|relayhost|smtpd_relay_restrictions|smtpd_recipient_restrictions|myhostname|mydestination' || echo 'postconf unavailable (postfix may not be the listener)'"
blank
probe_priv "which MTA package is installed" \
  "dpkg -l 2>/dev/null | grep -E '^ii +(postfix|exim4|sendmail|opensmtpd|nullmailer)' | awk '{print \$2, \$3}' || echo '(no common MTA package found)'"
blank
probe_sh "local banner on 25" \
  "timeout 5 bash -c 'exec 3<>/dev/tcp/127.0.0.1/25; head -1 <&3' 2>/dev/null || echo '(no banner / not reachable on loopback)'"

section "Port 30033 and other high ports"
note "30033 is TeamSpeak's file-transfer port. A TeamSpeak container on this"
note "host would explain it — confirm against the docker mapping above."
blank
probe_sh "high ports listening (>1024, excluding known app ports)" \
  "ss -ltn 2>/dev/null | awk 'NR>1 {print \$4}' | sed 's/.*://' | sort -un | awk '\$1>1024 && \$1!=5047 && \$1!=5048 && \$1!=5182 && \$1!=5432 && \$1!=8080'"

section "Unidentified loopback services"
note "Loopback-only services are not internet-reachable, but an unexplained one"
note "is still worth naming — it may be an agent-installed helper nobody kept."
blank
probe_priv "loopback listeners with process" \
  "ss -ltnp 2>/dev/null | grep -E '127\\.0\\.0\\.1|\\[::1\\]'"

section "api.torn.geromet.com — what does it actually serve?"
note "This hostname is declared in the live nginx config but appears nowhere in"
note "the repository. Worth knowing whether it is another public route to the"
note "production API, and whether DNS even points at this machine."
blank
probe_priv "server block" \
  "nginx -T 2>/dev/null | awk '/server[[:space:]]*\\{/{buf=\"\"} {buf=buf\"\\n\"\$0} /api\\.torn\\.geromet\\.com/{found=1} /^\\}/{if(found){print buf; found=0} buf=\"\"}' | grep -E 'server_name|listen|proxy_pass|root|return|location' || echo '(no block found)'"
blank
probe_sh "does it resolve, and to where?" \
  "getent hosts api.torn.geromet.com || echo '(does not resolve)'"
blank
probe_sh "does it reach this machine, and which upstream?" \
  "curl -s -o /dev/null -w 'https://api.torn.geromet.com/ -> %{http_code}\n' --max-time 10 https://api.torn.geromet.com/ 2>/dev/null || echo '(no response)'"
blank
note "If this proxies to 127.0.0.1:5047 it is a SECOND public entrance to the"
note "production API. Nothing in the application uses it — verified by grep and"
note "pinned by scripts/verify/devhost-contract.sh — so it can most likely be"
note "retired. Confirm before removing: something outside this repo may rely on it."

section "What the host believes about its own exposure"
probe_sh "primary IPv4/IPv6" "ip -brief addr show scope global 2>/dev/null || ip addr show 2>/dev/null | grep -E 'inet |inet6 ' | grep -v '127\\.0\\.0\\.1\\|::1'"
blank
note "A cloud firewall (Hetzner Cloud Firewall) sits outside this VM and cannot"
note "be observed from inside it. If one is configured, real exposure may be"
note "narrower than everything above suggests. Verify in the Hetzner console."
note "Conversely, nothing here can widen exposure that the cloud firewall blocks."

section "Automated assessment"
# Turn the raw data above into findings, so the reader is not left to correlate.
pub_ports="$(ss -ltn 2>/dev/null | awk 'NR>1 {print $4}' | grep -E '^(0\.0\.0\.0|\[::\]|\*):' | sed 's/.*://' | sort -un | tr '\n' ' ')"
note "publicly bound ports: ${pub_ports:-none}"
blank

for p in ${pub_ports}; do
  case "${p}" in
    80|443)
      finding INFO "port ${p} public — expected (nginx)" ;;
    25)
      finding HIGH "port 25 (SMTP) is bound to all interfaces — verify it is not an open relay, or bind it to loopback" ;;
    22)
      finding MED "port 22 public — SSH is otherwise loopback-only here via the cloudflared tunnel; a public :22 would be a regression" ;;
    *)
      finding MED "port ${p} public and not an expected web port — confirm it is intentional and firewalled" ;;
  esac
done

if (( ROOT_OK == 1 )); then
  if bash -c "${SUDO} iptables -S DOCKER-USER 2>/dev/null" | grep -qE '^-A DOCKER-USER (-j RETURN)?$|^-N DOCKER-USER$'; then
    if ! bash -c "${SUDO} iptables -S DOCKER-USER 2>/dev/null" | grep -qE 'DROP|REJECT'; then
      finding MED "DOCKER-USER chain has no DROP/REJECT rules — published container ports bypass ufw and are internet-reachable"
    fi
  fi
  if bash -c "${SUDO} ufw status 2>/dev/null" | grep -qi 'inactive'; then
    finding HIGH "ufw is inactive — the host relies entirely on the cloud firewall, if one exists"
  fi
else
  finding INFO "firewall and process attribution not assessed — collector ran without root"
fi

print_findings_summary

section "End"
note "Report complete. Nothing on this host was modified."
