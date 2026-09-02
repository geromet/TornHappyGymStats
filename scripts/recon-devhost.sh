#!/usr/bin/env bash
# recon-devhost.sh — Read-only survey of the server, scoped to what the
# torndev.geromet.com bootstrap needs to know.
#
# SCRIPT_CATEGORY=recon
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Runs ON the server. Writes a report to stdout and changes nothing: no installs,
# no systemctl start/enable/reload, no file writes outside the report itself.
# Every command here is a query.
#
# SECRET HANDLING — this report is meant to be read by someone other than the
# operator who ran it, so it never prints a secret value. Env files are reported
# as key names plus a status word (SET / PLACEHOLDER / EMPTY). Private keys are
# never read.
#
# Where it matters whether two secrets are the SAME (dev vs production database,
# token signing keys), the report prints a fingerprint of each value. That
# fingerprint is an HMAC-SHA256 under a random key generated per run and never
# printed — NOT a plain hash.
#
# A plain hash would be unsafe here. Connection strings have a highly
# predictable shape, so a bare digest of
#   Host=...;Database=...;Username=...;Password=<unknown>
# lets anyone holding the report grind password candidates offline until one
# matches. Keying the digest with a per-run secret removes that: fingerprints
# stay comparable WITHIN one report, and are meaningless outside it. It also
# means fingerprints from two different runs are not comparable — that is the
# intended trade.
#
# Requires scripts/lib/recon-common.sh prepended. Use the wrapper, which
# concatenates them locally and pipes the result over stdin:
#
#   bash scripts/recon-fetch.sh devhost --sudo
set -uo pipefail   # deliberately no -e: a failed probe must not abort the survey

report_header "HappyGymStats dev-host reconnaissance"

section "Ports the dev host wants (5147 API, 5282 Blazor)"
for port in 5147 5282; do
  if ss -ltn 2>/dev/null | grep -q ":${port} "; then
    note "port ${port}: IN USE  <- pick a different port and update the unit + nginx together"
    probe_sh "  holder" "ss -ltnp 2>/dev/null | grep ':${port} ' || echo '(process name needs root)'"
  else
    note "port ${port}: free"
  fi
done

section "All listening TCP sockets"
probe_sh "listeners" "${SUDO} ss -ltnp 2>/dev/null || ss -ltn 2>/dev/null || netstat -tln 2>/dev/null"

section "Known service ports (production)"
for entry in "5047:api" "5182:blazor" "5048:adminpanel" "8080:keycloak" "5432:postgres"; do
  port="${entry%%:*}"; name="${entry##*:}"
  if ss -ltn 2>/dev/null | grep -q ":${port} "; then
    note "${port} (${name}): listening"
  else
    note "${port} (${name}): not listening"
  fi
done

section "nginx — version and layout"
probe_sh "version" "nginx -v 2>&1"
probe_sh "sites-enabled" "ls -l /etc/nginx/sites-enabled/ 2>/dev/null || echo '(no sites-enabled directory)'"
probe_sh "conf.d" "ls -l /etc/nginx/conf.d/ 2>/dev/null || echo '(no conf.d directory)'"
note ""
note "Which layout is live decides DEPLOY_DEV_NGINX_USE_CONF_D for setup-devhost-server.sh."

section "nginx — server_name to upstream map"
# Which block currently answers for an unmatched host is what makes
# torndev.geromet.com fall through today.
probe_sh "server blocks" "${SUDO} nginx -T 2>/dev/null | grep -nE 'server_name|listen |proxy_pass|default_server' | sed 's/^ *//' || echo '(nginx -T needs root)'"

section "nginx — is a torndev block already installed?"
probe_sh "torndev references" "${SUDO} grep -rn 'torndev' /etc/nginx/ 2>/dev/null || echo '(none found)'"

section "TLS origin certificate"
# Only metadata. The private key is never read.
probe_sh "origin.pem subject/SAN/validity" "${SUDO} openssl x509 -in /etc/ssl/cloudflare/origin.pem -noout -subject -ext subjectAltName -dates 2>/dev/null || openssl x509 -in /etc/ssl/cloudflare/origin.pem -noout -subject -ext subjectAltName -dates 2>/dev/null || echo '(cert not readable)'"
printf '  key present (existence only):\n'
printf '    origin.key: %s\n' "$(file_state /etc/ssl/cloudflare/origin.key)"
note "(contents are never read)"

section "systemd — happygymstats units"
probe_sh "unit files" "systemctl list-unit-files 'happygymstats*' --no-pager 2>/dev/null || echo '(none)'"
probe_sh "current state" "systemctl list-units 'happygymstats*' --all --no-pager 2>/dev/null || echo '(none)'"

section "systemd — dev units present?"
for unit in happygymstats-api-dev.service happygymstats-blazor-dev.service; do
  if [[ -f "/etc/systemd/system/${unit}" ]]; then
    note "${unit}: INSTALLED"
    probe_sh "  ApiBaseUrl/URLS/EnvironmentFile" "grep -E 'ApiBaseUrl|ASPNETCORE_URLS|EnvironmentFile|RestrictToAdmins' '/etc/systemd/system/${unit}' 2>/dev/null || echo '(no matching lines)'"
  else
    note "${unit}: not installed"
  fi
done

section "Deploy roots"
for root in /var/www/happygymstats /var/www/happygymstats-blazor /var/www/happygymstats-adminpanel /var/www/happygymstats-dev /var/www/happygymstats-blazor-dev; do
  if [[ -d "${root}" ]]; then
    probe_sh "${root}" "ls -ld '${root}' '${root}/current' 2>/dev/null; echo 'releases:'; ls -1 '${root}/releases' 2>/dev/null | tail -3 || echo '  (no releases dir)'"
  else
    note "${root}: absent"
  fi
done

section "API environment files — KEY NAMES AND STATUS ONLY"
note "Values are never printed. Status is derived, and identical-secret checks"
note "use a keyed fingerprint so two files can be compared without disclosure."
note ""

declare -A ENV_DIGESTS=()

describe_env_file() {
  local path="$1"
  local state
  state="$(file_state "${path}")"
  if [[ "${state}" != present* ]]; then
    note "${path}: ${state}"
    return
  fi

  local content
  if ! content="$(bash -c "${SUDO} cat '${path}'" 2>/dev/null)"; then
    note "${path}: present but BLIND — needs root"
    return
  fi

  note "${path}: present"
  while IFS= read -r line; do
    [[ -z "${line}" || "${line}" =~ ^[[:space:]]*# ]] && continue
    [[ "${line}" != *=* ]] && continue

    local key="${line%%=*}"
    local value="${line#*=}"
    local status digest

    if [[ -z "${value}" ]]; then
      status="EMPTY"
    elif [[ "${value}" == *REPLACE_ME* ]]; then
      status="PLACEHOLDER (must be edited before the service will work)"
    else
      status="SET"
    fi

    digest="$(fingerprint "${value}")"
    printf '    %-40s %-12s fp:%s\n' "${key}" "${status}" "${digest}"

    # Remember the digest so the isolation check below can compare files
    # without either value ever being held, printed, or logged.
    ENV_DIGESTS["${path}|${key}"]="${digest:-unavailable}"
  done <<< "${content}"
}

describe_env_file /etc/happygymstats/api.env
note ""
describe_env_file /etc/happygymstats/api-dev.env

section "Isolation check — dev must not share production secrets"
note "Compares keyed fingerprints only (HMAC under a random per-run key that is"
note "never printed). Neither secret value appears anywhere in this report."
note ""
isolation_problems=0
for key in ConnectionStrings__HappyGymStats ProvisionalToken__SigningKey HAPPYGYMSTATS_CONNECTION_STRING; do
  prod="${ENV_DIGESTS[/etc/happygymstats/api.env|${key}]:-}"
  dev="${ENV_DIGESTS[/etc/happygymstats/api-dev.env|${key}]:-}"

  if [[ -z "${prod}" && -z "${dev}" ]]; then
    continue
  fi

  if [[ -z "${dev}" ]]; then
    printf '    %-40s dev value ABSENT\n' "${key}"
    continue
  fi

  if [[ -z "${prod}" ]]; then
    printf '    %-40s OK (no production counterpart to collide with)\n' "${key}"
    continue
  fi

  if [[ "${prod}" == "${dev}" ]]; then
    printf '    %-40s *** SHARED WITH PRODUCTION *** (identical fingerprint)\n' "${key}"
    isolation_problems=$((isolation_problems + 1))
  else
    printf '    %-40s OK (dev fp:%s differs from prod fp:%s)\n' "${key}" "${dev}" "${prod}"
  fi
done

note ""
if (( isolation_problems > 0 )); then
  note "VERDICT: NOT ISOLATED — ${isolation_problems} secret(s) shared with production."
  note "A dev API on a shared connection string runs migrations against the"
  note "production schema; a shared signing key makes dev-minted tokens valid there."
  note "Fix before starting happygymstats-api-dev."
else
  note "VERDICT: no shared secrets detected between dev and production."
fi

section "Postgres — database inventory (names only)"
probe_sh "via docker" "${SUDO} docker ps --format '{{.Names}}\t{{.Image}}\t{{.Status}}' 2>/dev/null | grep -i postgres || echo '(no postgres container visible)'"
probe_sh "database list" "${SUDO} docker exec \$(${SUDO} docker ps --format '{{.Names}}' 2>/dev/null | grep -i postgres | head -1) psql -U postgres -Atc '\\l' 2>/dev/null | cut -d'|' -f1 || psql -U postgres -Atc '\\l' 2>/dev/null | cut -d'|' -f1 || echo '(cannot enumerate databases)'"

section "Keycloak"
probe_sh "container" "${SUDO} docker ps --format '{{.Names}}\t{{.Image}}\t{{.Status}}' 2>/dev/null | grep -i keycloak || echo '(no keycloak container visible)'"
probe_sh "local health" "curl -fsS -o /dev/null -w 'http://127.0.0.1:8080 -> %{http_code}\n' --max-time 10 http://127.0.0.1:8080/realms/torn/.well-known/openid-configuration 2>&1 || echo '(no response on 8080)'"
note ""
note "Client inventory needs Keycloak admin credentials and is intentionally"
note "NOT collected here. Confirm 'happygymstats-web-dev' exists in the console."

section "Loopback health of existing services"
for entry in "5047:/api/v1/torn/health:api" "5048:/admin/health:adminpanel"; do
  port="${entry%%:*}"; rest="${entry#*:}"; path="${rest%%:*}"; name="${rest##*:}"
  probe_sh "${name} (127.0.0.1:${port}${path})" "curl -fsS -o /dev/null -w '%{http_code}\n' --max-time 10 'http://127.0.0.1:${port}${path}' 2>&1 || echo 'no response'"
done

section "Docker overview"
probe_sh "version" "docker --version 2>&1"
probe_sh "containers" "${SUDO} docker ps -a --format '  {{.Names}}\t{{.Status}}\t{{.Image}}' 2>/dev/null || echo '(cannot list)'"

section "Host capacity"
probe_sh "memory" "free -h 2>/dev/null | grep -E '^(Mem|Swap):'"
probe_sh "disk" "df -h / /var 2>/dev/null | grep -v '^Filesystem'"
probe_sh "cpu" "nproc 2>/dev/null"
probe_sh "os" "grep -E '^(NAME|VERSION)=' /etc/os-release 2>/dev/null"

print_findings_summary

section "End"
note "Report complete. Nothing on this host was modified."
