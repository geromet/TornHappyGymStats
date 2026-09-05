#!/usr/bin/env bash
# screenshot-board.sh — look at the running app instead of guessing from markup.
#
# SCRIPT_CATEGORY=ux
# SCRIPT_MUTATES_SERVER_STATE=0
#
# Starts the API and the Blazor host locally with development authentication and
# the seeded war, screenshots the board at every viewport and theme, then stops
# both. Nothing touches the server; nothing touches a browser you use yourself.
#
# WHY THIS EXISTS. U001 shipped a caption reading "Last hit (inferred) inferred"
# and an operator diagnostic in an error banner, both invisible in the source and
# obvious in the first rendered frame. A UX slice is not done until someone has
# looked at it, and "someone" should not have to be a person.
#
# BOTH HOSTS NEED DEV AUTH. With it set only on the frontend the board renders
# "War board unavailable. Authentication is required" — the dev-header principal
# has no access token to forward to the API. That cost twenty minutes once.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly VENV="${ROOT_DIR}/.venv"
readonly DRIVER="${SCRIPT_DIR}/ux/shoot.py"

: "${SHOT_API_PORT:=5047}"
: "${SHOT_WEB_PORT:=5137}"
: "${SHOT_ROUTE:=/war}"
: "${SHOT_OUT_DIR:=${ROOT_DIR}/workspace/tmp/screenshots}"

API_PID=""
WEB_PID=""

usage() {
  cat <<'EOF'
Usage:
  bash scripts/screenshot-board.sh [--route /war] [--out DIR] [--keep-running]
  bash scripts/screenshot-board.sh --setup     install Playwright + its Chromium
  bash scripts/screenshot-board.sh --check     report whether the tooling is ready

Screenshots the local app at phone/tablet/desktop, light and dark.

Environment:
  SHOT_ROUTE     page to shoot (default /war)
  SHOT_NO_WAR    1 = start with no war seeded, to shoot the empty board
  SHOT_OUT_DIR   default workspace/tmp/screenshots
  SHOT_API_PORT  default 5047
  SHOT_WEB_PORT  default 5137

Playwright lives in .venv/ (gitignored) with its own Chromium under
~/.cache/ms-playwright. No sudo, and no browser you use yourself is involved.
EOF
}

cleanup() {
  local rc=$?
  if [[ "${KEEP_RUNNING:-0}" != "1" ]]; then
    [[ -n "${WEB_PID}" ]] && kill "${WEB_PID}" 2>/dev/null || true
    [[ -n "${API_PID}" ]] && kill "${API_PID}" 2>/dev/null || true
    # Give them a moment to release the ports, so a re-run does not fail on a
    # port still held by the process we just asked to stop.
    sleep 2
  else
    echo "==> Left running: API :${SHOT_API_PORT}, web :${SHOT_WEB_PORT} (pids ${API_PID} ${WEB_PID})"
  fi
  exit "${rc}"
}

setup() {
  echo "==> Creating ${VENV#"${ROOT_DIR}/"} and installing Playwright"
  python3 -m venv "${VENV}"
  "${VENV}/bin/python" -m pip install --quiet --upgrade pip
  "${VENV}/bin/pip" install --quiet playwright
  echo "==> Downloading Chromium (~115 MB, into ~/.cache/ms-playwright)"
  "${VENV}/bin/playwright" install chromium
  echo "==> Ready."
}

check() {
  local ok=0
  if [[ -x "${VENV}/bin/python" ]] && "${VENV}/bin/python" -c 'import playwright' 2>/dev/null; then
    echo "  ok   playwright installed in .venv"
  else
    echo "  !!   playwright missing — run: bash scripts/screenshot-board.sh --setup"
    ok=1
  fi
  if compgen -G "${HOME}/.cache/ms-playwright/chromium*" >/dev/null; then
    echo "  ok   chromium present in ~/.cache/ms-playwright"
  else
    echo "  !!   chromium missing — run: bash scripts/screenshot-board.sh --setup"
    ok=1
  fi
  [[ -f "${DRIVER}" ]] && echo "  ok   driver present" || { echo "  !!   missing ${DRIVER#"${ROOT_DIR}/"}"; ok=1; }
  return "${ok}"
}

# 127.0.0.1 everywhere, deliberately. Kestrel binding "localhost" listens on one
# stack; Chromium resolving "localhost" may pick the other and get
# ERR_CONNECTION_REFUSED against a server curl can reach happily. Using the
# literal address on both sides removes the question.
#
# Readiness means "the server answered", not "the server answered 200".
# curl -f treats 401 and 404 as failure, so do not use it here. A completed curl
# with any real HTTP status is proof of life; a transport failure is not.
wait_for() {
  local url="$1" name="$2" deadline=$((SECONDS + 180)) code
  while (( SECONDS < deadline )); do
    if code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 "${url}")"; then
      [[ "${code}" != "000" ]] && return 0
    fi
    sleep 2
  done
  echo "FAIL: ${name} never answered at ${url} within 180s" >&2
  return 1
}

KEEP_RUNNING=0
EXTRA_ARGS=()
while (( $# > 0 )); do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --setup) setup; exit 0 ;;
    --check) check; exit $? ;;
    --keep-running) KEEP_RUNNING=1; shift ;;
    --route) SHOT_ROUTE="$2"; shift 2 ;;
    --out) SHOT_OUT_DIR="$2"; shift 2 ;;
    --viewport|--theme) EXTRA_ARGS+=("$1" "$2"); shift 2 ;;
    *) echo "Unknown option '$1'. Try --help." >&2; exit 2 ;;
  esac
done

check >/dev/null || { echo "Tooling not ready:"; check; exit 1; }

trap cleanup EXIT INT TERM

readonly API_BIN="${ROOT_DIR}/src/HappyGymStats.Api/bin/Debug/net10.0/HappyGymStats.Api"
readonly WEB_BIN="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/bin/Debug/net10.0/HappyGymStats.Blazor"

port_free() {
  ! ss -ltn "sport = :$1" 2>/dev/null | grep -q LISTEN
}

for port in "${SHOT_API_PORT}" "${SHOT_WEB_PORT}"; do
  port_free "${port}" || {
    echo "FAIL: port ${port} is already in use. Stop whatever holds it and re-run." >&2
    exit 1
  }
done

LOG_DIR="$(mktemp -d)"

# Build first, then run the BUILT BINARIES rather than `dotnet run`.
#
# `dotnet run` is a wrapper that builds and then launches the app as a child, so
# $! is the wrapper's pid: killing it can leave the real server holding the port,
# and its output can vanish entirely. An earlier version of this script did that
# and produced empty logs, a port nobody owned, and a browser getting
# ERR_CONNECTION_REFUSED against a server that had never started.
echo "==> Building"
dotnet build "${ROOT_DIR}/HappyGymStats.sln" -v q --nologo > "${LOG_DIR}/build.log" 2>&1 || {
  tail -20 "${LOG_DIR}/build.log"
  echo "FAIL: build failed" >&2
  exit 1
}

if [[ "${SHOT_NO_WAR:-0}" == "1" ]]; then
  echo "==> Starting API on :${SHOT_API_PORT} (development auth, NO war seeded)"
else
  echo "==> Starting API on :${SHOT_API_PORT} (development auth, seeded war)"
fi
ASPNETCORE_ENVIRONMENT=Development \
HAPPYGYMSTATS_DEV_AUTH=1 \
HAPPYGYMSTATS_DEV_SKIP_WAR_SEED="${SHOT_NO_WAR:-0}" \
ASPNETCORE_URLS="http://127.0.0.1:${SHOT_API_PORT}" \
  "${API_BIN}" > "${LOG_DIR}/api.log" 2>&1 &
API_PID=$!

wait_for "http://127.0.0.1:${SHOT_API_PORT}/" "API" || { tail -20 "${LOG_DIR}/api.log"; exit 1; }

echo "==> Starting Blazor host on :${SHOT_WEB_PORT}"
ASPNETCORE_ENVIRONMENT=Development \
HAPPYGYMSTATS_DEV_AUTH=1 \
ApiBaseUrl="http://127.0.0.1:${SHOT_API_PORT}" \
ASPNETCORE_URLS="http://127.0.0.1:${SHOT_WEB_PORT}" \
  "${WEB_BIN}" > "${LOG_DIR}/web.log" 2>&1 &
WEB_PID=$!

wait_for "http://127.0.0.1:${SHOT_WEB_PORT}/" "Blazor host" || { tail -20 "${LOG_DIR}/web.log"; exit 1; }

echo "==> Shooting ${SHOT_ROUTE}"
"${VENV}/bin/python" "${DRIVER}" \
  --base-url "http://127.0.0.1:${SHOT_WEB_PORT}" \
  --route "${SHOT_ROUTE}" \
  --out "${SHOT_OUT_DIR}" \
  "${EXTRA_ARGS[@]}"

echo
echo "==> Logs from this run: ${LOG_DIR}"
