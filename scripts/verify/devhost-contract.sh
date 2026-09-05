#!/usr/bin/env bash
# devhost-contract.sh — Static, offline contract for the torndev.geromet.com dev host.
#
# The dev host's whole purpose is isolation from production. The invariants that
# guarantee it are one-line edits away from being broken, and breaking them is
# silent: a dev frontend pointed at the production API looks completely normal
# until it writes. This gate is what makes that mistake loud.
#
# Runs offline. No SSH, no Docker, no live host required.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${ROOT_DIR}"

readonly NGINX_DEV="infra/nginx-torndev.conf"
readonly NGINX_PROD="infra/torn.conf"
readonly UNIT_API_DEV="infra/happygymstats-api-dev.service"
readonly UNIT_BLAZOR_DEV="infra/happygymstats-blazor-dev.service"
readonly UNIT_API_PROD="infra/happygymstats-api.service"
readonly SETUP_SCRIPT="scripts/setup-devhost-server.sh"
readonly DEPLOY_SCRIPT="scripts/deploy-dev.sh"
readonly ACCESS_SOURCE="src/HappyGymStats.Identity/Authentication/RestrictedAccessExtensions.cs"
readonly BLAZOR_PROGRAM="src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs"

# Production values the dev host must never reuse.
readonly PROD_API_PORT="5047"
readonly PROD_BLAZOR_PORT="5182"
readonly PROD_ADMIN_PORT="5048"
readonly DEV_API_PORT="5147"
readonly DEV_BLAZOR_PORT="5282"

failures=0

fail() {
  echo "[FAIL] $1" >&2
  failures=$((failures + 1))
}

pass() {
  echo "[PASS] $1"
}

require_file() {
  if [[ -f "$1" ]]; then
    pass "file exists: $1"
  else
    fail "missing required file: $1"
  fi
}

require_contains() {
  local path="$1" needle="$2" label="$3"
  if [[ ! -f "${path}" ]]; then
    fail "${label} (file missing: ${path})"
    return
  fi
  if grep -Fq -- "${needle}" "${path}"; then
    pass "${label}"
  else
    fail "${label} (expected '${needle}' in ${path})"
  fi
}

require_absent() {
  local path="$1" needle="$2" label="$3"
  if [[ ! -f "${path}" ]]; then
    fail "${label} (file missing: ${path})"
    return
  fi
  if grep -Fq -- "${needle}" "${path}"; then
    fail "${label} (found forbidden '${needle}' in ${path})"
  else
    pass "${label}"
  fi
}

echo "==> devhost verify: files present"
for f in "${NGINX_DEV}" "${UNIT_API_DEV}" "${UNIT_BLAZOR_DEV}" "${SETUP_SCRIPT}" "${DEPLOY_SCRIPT}" "${ACCESS_SOURCE}"; do
  require_file "${f}"
done

echo "==> devhost verify: shell syntax"
for f in "${SETUP_SCRIPT}" "${DEPLOY_SCRIPT}" "scripts/verify/devhost-smoke.sh"; do
  if [[ -f "${f}" ]]; then
    if bash -n "${f}" 2>/dev/null; then
      pass "bash -n clean: ${f}"
    else
      fail "bash -n failed: ${f}"
    fi
  else
    fail "missing script: ${f}"
  fi
done

echo "==> devhost verify: dev ports are distinct from production"
require_contains "${UNIT_API_DEV}" "ASPNETCORE_URLS=http://127.0.0.1:${DEV_API_PORT}" "dev API binds ${DEV_API_PORT}"
require_contains "${UNIT_BLAZOR_DEV}" "ASPNETCORE_URLS=http://127.0.0.1:${DEV_BLAZOR_PORT}" "dev Blazor binds ${DEV_BLAZOR_PORT}"
require_absent "${UNIT_API_DEV}" "127.0.0.1:${PROD_API_PORT}" "dev API unit does not reference prod API port ${PROD_API_PORT}"
require_absent "${UNIT_API_DEV}" "127.0.0.1:${PROD_BLAZOR_PORT}" "dev API unit does not reference prod Blazor port ${PROD_BLAZOR_PORT}"
require_absent "${UNIT_API_DEV}" "127.0.0.1:${PROD_ADMIN_PORT}" "dev API unit does not reference prod AdminPanel port ${PROD_ADMIN_PORT}"

echo "==> devhost verify: dev Blazor drives the dev API, never production"
# The highest-consequence line in the whole change. ApiBaseUrl is a plain
# systemd Environment= value; setting it to 5047 makes the dev frontend read and
# write production data while every other signal still says "dev".
require_contains "${UNIT_BLAZOR_DEV}" "ApiBaseUrl=http://127.0.0.1:${DEV_API_PORT}" "dev Blazor ApiBaseUrl points at dev API ${DEV_API_PORT}"
require_absent "${UNIT_BLAZOR_DEV}" "ApiBaseUrl=http://127.0.0.1:${PROD_API_PORT}" "dev Blazor ApiBaseUrl is not the production API"

echo "==> devhost verify: dev services do not share production state"
require_contains "${UNIT_API_DEV}" "EnvironmentFile=/etc/happygymstats/api-dev.env" "dev API uses its own EnvironmentFile"
require_absent "${UNIT_API_DEV}" "EnvironmentFile=/etc/happygymstats/api.env" "dev API does not source the production env file"
require_contains "${UNIT_API_DEV}" "WorkingDirectory=/var/www/happygymstats-dev/current" "dev API uses the dev release root"
require_contains "${UNIT_BLAZOR_DEV}" "WorkingDirectory=/var/www/happygymstats-blazor-dev/current" "dev Blazor uses the dev release root"
require_contains "${UNIT_BLAZOR_DEV}" "EnvironmentFile=/etc/happygymstats/blazor-dev.env" "dev Blazor uses its own EnvironmentFile"
# Sourcing production's blazor.env would hand the dev host the production
# client secret — the credential the separate client exists to keep apart.
require_absent "${UNIT_BLAZOR_DEV}" "EnvironmentFile=/etc/happygymstats/blazor.env" "dev Blazor does not source the production Blazor env file"
# A secret in a 0644 unit file is readable by every account on the host.
require_absent "${UNIT_BLAZOR_DEV}" "Environment=Keycloak__ClientSecret" "dev Blazor keeps the client secret out of the unit file"
require_absent "${UNIT_API_DEV}" "Environment=Keycloak__ClientSecret" "dev API keeps the client secret out of the unit file"
require_contains "${UNIT_BLAZOR_DEV}" "Keycloak__RequireClientSecret=true" "dev Blazor fails fast when the client secret is missing"
require_contains "${UNIT_API_DEV}" "Keycloak__Audience=happygymstats-api-dev" "dev API accepts only the dev audience"

echo "==> devhost verify: nginx server block"
require_contains "${NGINX_DEV}" "server_name torndev.geromet.com;" "dev nginx serves torndev.geromet.com"
require_absent "${NGINX_DEV}" "server_name torn.geromet.com;" "dev nginx does not claim the production host"
require_contains "${NGINX_DEV}" "proxy_pass         http://127.0.0.1:${DEV_API_PORT};" "dev nginx proxies API to ${DEV_API_PORT}"
require_contains "${NGINX_DEV}" "proxy_pass         http://127.0.0.1:${DEV_BLAZOR_PORT};" "dev nginx proxies Blazor to ${DEV_BLAZOR_PORT}"
require_absent "${NGINX_DEV}" "127.0.0.1:${PROD_API_PORT}" "dev nginx never proxies to the production API"
require_absent "${NGINX_DEV}" "127.0.0.1:${PROD_BLAZOR_PORT}" "dev nginx never proxies to the production Blazor"
# The origin certificate covers *.geromet.com but not *.*.geromet.com, so a
# multi-label host would fail TLS. Keep the dev host a single label.
if grep -qE 'server_name +[A-Za-z0-9-]+\.[A-Za-z0-9-]+\.geromet\.com;' "${NGINX_DEV}"; then
  fail "dev nginx uses a multi-label subdomain; the *.geromet.com origin cert does not cover it"
else
  pass "dev host is a single-label subdomain (origin cert covers it)"
fi

echo "==> devhost verify: production config untouched"
require_contains "${NGINX_PROD}" "server_name torn.geromet.com;" "production nginx still serves torn.geromet.com"
require_contains "${UNIT_API_PROD}" "ASPNETCORE_URLS=http://127.0.0.1:${PROD_API_PORT}" "production API still binds ${PROD_API_PORT}"
require_absent "${UNIT_API_PROD}" "Access__RestrictToAdmins" "production API unit does not enable the admin-only gate"
if [[ -f "infra/happygymstats-blazor.service" ]]; then
  require_absent "infra/happygymstats-blazor.service" "Access__RestrictToAdmins" "production Blazor unit does not enable the admin-only gate"
  require_absent "infra/happygymstats-blazor.service" "Environment=Keycloak__ClientSecret" "production Blazor keeps the client secret out of the unit file"
  require_absent "infra/happygymstats-blazor.service" "Keycloak__ClientId=happygymstats-web-dev" "production Blazor does not use the dev Keycloak client"
fi

echo "==> devhost verify: admin-only gate is opt-in and wired"
require_contains "${UNIT_BLAZOR_DEV}" "Access__RestrictToAdmins=true" "dev Blazor enables the admin-only gate"
require_contains "${BLAZOR_PROGRAM}" "UseAdminOnlyAccessWhenConfigured" "Blazor pipeline calls the admin-only gate"
require_contains "${ACCESS_SOURCE}" "Access:RestrictToAdmins" "gate reads Access:RestrictToAdmins"
# A gate that swallowed the OIDC round trip would lock the host out entirely.
for allowed in "/signin-oidc" "/login" "/auth/"; do
  require_contains "${ACCESS_SOURCE}" "\"${allowed}\"" "sign-in path stays reachable: ${allowed}"
done

echo "==> devhost verify: the API base is a single loopback choke point"
# Everything the Blazor host talks to is derived from ApiBaseUrl: all four
# HttpClients, and the SignalR hub URL (WarBoardService builds it as
# new Uri(http.BaseAddress, "/api/hub/war")). So ApiBaseUrl is the only place
# dev could be pointed at production, and it must never be a public hostname.
#
# This matters because the live server has an api.torn.geromet.com server block
# that is absent from this repo. Nothing references it today; this check is what
# keeps that true.
bad_base=0
while IFS= read -r line; do
  [[ -z "${line}" ]] && continue
  value="${line#*ApiBaseUrl}"
  if printf '%s' "${value}" | grep -qE 'https?://[A-Za-z0-9.-]*geromet\.com'; then
    fail "ApiBaseUrl points at a public geromet.com host: ${line}"
    bad_base=1
  fi
done < <(grep -rn "ApiBaseUrl" --include=*.json --include=*.service infra/ src/ 2>/dev/null | grep -v '/obj/\|/bin/')
if (( bad_base == 0 )); then
  pass "no ApiBaseUrl resolves to a public geromet.com host (all loopback/localhost)"
fi

# A browser-side HttpClient would bypass ApiBaseUrl entirely and resolve against
# whatever host the page was served from — or worse, an absolute URL. The WASM
# client currently registers none; keep it that way.
if grep -rqn "BaseAddress" --include=*.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/ 2>/dev/null; then
  fail "the WASM client now sets an HttpClient BaseAddress — browser-side calls bypass ApiBaseUrl and can cross environments"
else
  pass "WASM client registers no HttpClient (no browser-side call can bypass ApiBaseUrl)"
fi

# api.torn.com is Torn's own API and is expected. Any *.geromet.com absolute
# base address is not.
if grep -rn "BaseAddress *= *new Uri(\"" --include=*.cs src/ 2>/dev/null | grep -v '/obj/\|/bin/' | grep -qE 'geromet\.com'; then
  fail "an HttpClient BaseAddress hardcodes a geromet.com host"
else
  pass "no HttpClient hardcodes a geromet.com base address"
fi

echo "==> devhost verify: dev deploy cannot target production"
require_contains "${DEPLOY_SCRIPT}" "DEPLOY_FORBID_PRODUCTION_TARGET=1" "dev deploy arms the production-target refusal in both child scripts"
require_contains "scripts/deploy-backend.sh" "DEPLOY_FORBID_PRODUCTION_TARGET" "backend deploy honours the refusal"
require_contains "scripts/deploy-frontend.sh" "DEPLOY_FORBID_PRODUCTION_TARGET" "frontend deploy honours the refusal"
require_contains "scripts/deploy-config.sh" "_deploy_env_snapshot" "exported overrides survive .env.deploy"
require_contains "${DEPLOY_SCRIPT}" "production_target_refused" "deploy-dev.sh refuses production roots/units"
require_contains "${SETUP_SCRIPT}" "DEPLOY_INSTALL_DEV_HOST" "setup script is gated behind DEPLOY_INSTALL_DEV_HOST"
require_contains "${SETUP_SCRIPT}" "--confirm-remote-setup" "setup script requires explicit remote confirmation"

echo
if (( failures > 0 )); then
  echo "DEVHOST_CONTRACT_FAIL failures=${failures}" >&2
  exit 1
fi

echo "DEVHOST_CONTRACT_PASS failures=0"
