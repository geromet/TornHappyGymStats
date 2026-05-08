#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly ADMIN_CONF="${ROOT_DIR}/infra/nginx-adminpanel.conf"

fail() {
  echo "S04_VERIFY_FAIL: $*" >&2
  exit 1
}

assert_contains() {
  local file="$1"
  local needle="$2"
  if ! grep -Fq "$needle" "$file"; then
    fail "missing_token file=${file} token=${needle}"
  fi
}

assert_not_contains() {
  local file="$1"
  local needle="$2"
  if grep -Fq "$needle" "$file"; then
    fail "unexpected_token file=${file} token=${needle}"
  fi
}

[[ -f "${ADMIN_CONF}" ]] || fail "missing_file path=${ADMIN_CONF}"

echo "==> S04 verify: required nginx admin host tokens"
assert_contains "${ADMIN_CONF}" "server_name admin.geromet.com;"
assert_contains "${ADMIN_CONF}" "listen 443 ssl http2;"
assert_contains "${ADMIN_CONF}" "ssl_certificate     /etc/ssl/cloudflare/origin.pem;"
assert_contains "${ADMIN_CONF}" "ssl_certificate_key /etc/ssl/cloudflare/origin.key;"
assert_contains "${ADMIN_CONF}" "location = /admin/health {"
assert_contains "${ADMIN_CONF}" "location /admin/ {"
assert_contains "${ADMIN_CONF}" "proxy_pass         http://127.0.0.1:5048;"
assert_contains "${ADMIN_CONF}" 'proxy_set_header   Host              $host;'
assert_contains "${ADMIN_CONF}" 'proxy_set_header   X-Real-IP         $remote_addr;'
assert_contains "${ADMIN_CONF}" 'proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;'
assert_contains "${ADMIN_CONF}" "proxy_set_header   X-Forwarded-Proto https;"
assert_contains "${ADMIN_CONF}" "listen 80;"
assert_contains "${ADMIN_CONF}" 'return 301 https://$host$request_uri;'

echo "==> S04 verify: route-collision guard tokens"
assert_not_contains "${ADMIN_CONF}" "server_name torn.geromet.com;"
assert_not_contains "${ADMIN_CONF}" "server_name auth.geromet.com;"
assert_not_contains "${ADMIN_CONF}" "location /api/ {"
assert_not_contains "${ADMIN_CONF}" "proxy_set_header   Upgrade"
assert_not_contains "${ADMIN_CONF}" "proxy_set_header   Connection        \"upgrade\""

if command -v nginx >/dev/null 2>&1; then
  echo "==> S04 verify: nginx syntax check"
  tmp_conf="$(mktemp)"
  trap 'rm -f "${tmp_conf}"' EXIT
  cat >"${tmp_conf}" <<EOF
worker_processes 1;
pid /tmp/nginx-s04.pid;
error_log /tmp/nginx-s04-error.log;
events { worker_connections 16; }
http {
  include ${ADMIN_CONF};
}
EOF

  nginx -t -c "${tmp_conf}"
else
  echo "S04_VERIFY_WARN: nginx_not_installed skipping nginx -t"
fi

echo "S04_VERIFY_PASS: adminpanel nginx config checks passed"
