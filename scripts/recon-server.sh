#!/bin/bash
set -euo pipefail

HR="───────────────────────────────────────"

section() { echo; echo "$HR"; echo "  $1"; echo "$HR"; }

section "OS"
uname -r
grep -E "^(NAME|VERSION)=" /etc/os-release

section "CPU & Memory"
echo "CPUs:      $(nproc)"
echo "RAM:"
free -h | grep -E "^(Mem|Swap):"
echo "Disk (/):"
df -h / | tail -1

section "Docker"
docker --version 2>/dev/null || echo "  docker: NOT installed"
(docker compose version 2>/dev/null) || (docker-compose --version 2>/dev/null) || echo "  docker-compose/plugin: NOT found"
echo "Containers:"
docker ps -a --format "  {{.Names}}\t{{.Status}}\t{{.Image}}" 2>/dev/null || echo "  (cannot list — not in docker group, or docker not running)"
echo "Networks:"
docker network ls --format "  {{.Name}}\t{{.Driver}}" 2>/dev/null || echo "  (unavailable)"

section "Ports in use"
ss -tlnp 2>/dev/null | awk 'NR>1 {print "  "$0}' || netstat -tlnp 2>/dev/null | awk 'NR>2 {print "  "$0}'

section "Systemd services (running)"
systemctl list-units --type=service --state=running --no-pager --no-legend | awk '{print "  "$0}'

section "Nginx"
nginx -v 2>&1 || echo "  nginx: NOT installed"
echo "Enabled sites/configs:"
(ls -1 /etc/nginx/sites-enabled/ 2>/dev/null | awk '{print "  "$0}') || \
(ls -1 /etc/nginx/conf.d/ 2>/dev/null | awk '{print "  "$0}') || \
echo "  (none found)"

section "Disk summary"
df -h | grep -v "^tmpfs\|^udev\|^overlay" | awk '{print "  "$0}'

section "Keycloak feasibility"
TOTAL_MEM=$(free -m | awk '/^Mem:/{print $2}')
FREE_MEM=$(free -m | awk '/^Mem:/{print $7}')
echo "  Total RAM:     ${TOTAL_MEM} MB"
echo "  Available RAM: ${FREE_MEM} MB"
if [ "$FREE_MEM" -ge 1500 ]; then
  echo "  Status: OK — sufficient for Keycloak container (~512MB heap, 1GB headroom)"
elif [ "$FREE_MEM" -ge 800 ]; then
  echo "  Status: MARGINAL — Keycloak needs JVM tuning (JAVA_OPTS=-Xmx512m)"
else
  echo "  Status: LOW — consider VPS upgrade before adding Keycloak"
fi

FREE_DISK=$(df -BG / | awk 'NR==2{gsub("G",""); print $4}')
echo "  Free disk (/): ${FREE_DISK} GB"
if [ "$FREE_DISK" -ge 10 ]; then
  echo "  Disk: OK for Keycloak + Postgres volumes"
else
  echo "  Disk: LOW — check usage before adding containers"
fi

section "LUKS / encrypted volumes"
lsblk -o NAME,TYPE,FSTYPE,MOUNTPOINT 2>/dev/null | awk '{print "  "$0}' || echo "  (lsblk unavailable)"
echo "  (Look for TYPE=crypt rows above — that indicates LUKS already in use)"

echo
echo "Done. Paste full output back to Claude."
