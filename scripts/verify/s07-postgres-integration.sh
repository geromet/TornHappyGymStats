#!/usr/bin/env bash
# s07-postgres-integration.sh — Run Postgres provider integration tests with explicit skip/failure semantics.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly SKIP_ENV_VAR="HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION"
readonly TIMEOUT_ENV_VAR="HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS"
readonly DEFAULT_TIMEOUT_SECONDS=90
readonly TEST_FILTER='Category=PostgresApiIntegration'

cd "${ROOT_DIR}"

if [[ "${!SKIP_ENV_VAR:-}" =~ ^(1|true|TRUE|yes|YES)$ ]]; then
  echo "SKIP: ${SKIP_ENV_VAR} is set; Postgres integration verifier intentionally skipped."
  exit 0
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "SKIP: docker CLI not found; Postgres integration tests require Docker/Testcontainers."
  echo "      Install/start Docker or set ${SKIP_ENV_VAR}=1 for intentional skip."
  exit 0
fi

if ! docker info >/dev/null 2>&1; then
  echo "SKIP: Docker daemon unavailable/unhealthy; cannot run Postgres integration tests."
  echo "      Start Docker, then re-run, or set ${SKIP_ENV_VAR}=1 for intentional skip."
  exit 0
fi

startup_timeout="${!TIMEOUT_ENV_VAR:-$DEFAULT_TIMEOUT_SECONDS}"
if ! [[ "${startup_timeout}" =~ ^[0-9]+$ ]] || (( startup_timeout < 15 || startup_timeout > 600 )); then
  echo "WARN: ${TIMEOUT_ENV_VAR}='${startup_timeout}' invalid; expected 15-600 seconds. Using ${DEFAULT_TIMEOUT_SECONDS}."
  startup_timeout="${DEFAULT_TIMEOUT_SECONDS}"
fi

if command -v timeout >/dev/null 2>&1; then
  echo "RUN: dotnet test --filter \"${TEST_FILTER}\" (timeout ${startup_timeout}s)"
  if timeout "${startup_timeout}" dotnet test --filter "${TEST_FILTER}"; then
    echo "PASS: Postgres integration verifier passed."
    exit 0
  fi

  status=$?
  if [[ ${status} -eq 124 ]]; then
    echo "FAIL: dotnet test timed out after ${startup_timeout}s."
    echo "      Increase ${TIMEOUT_ENV_VAR} for slower machines or inspect Docker health."
    exit 124
  fi

  echo "FAIL: Postgres integration tests failed (exit ${status})."
  exit ${status}
fi

echo "RUN: dotnet test --filter \"${TEST_FILTER}\" (no timeout binary; relying on test-level timeout)"
dotnet test --filter "${TEST_FILTER}"
echo "PASS: Postgres integration verifier passed."
