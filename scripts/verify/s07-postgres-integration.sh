#!/usr/bin/env bash
# s07-postgres-integration.sh — Run Postgres provider integration tests with explicit skip/failure semantics.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly SKIP_ENV_VAR="HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION"
readonly REQUIRE_ENV_VAR="HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION"
readonly TIMEOUT_ENV_VAR="HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS"
readonly DEFAULT_TIMEOUT_SECONDS=90
readonly TEST_FILTER='Category=PostgresApiIntegration'

# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"
verify_require_commands dotnet grep sed tee mktemp

is_true() {
  [[ "${1:-}" =~ ^(1|true|TRUE|yes|YES)$ ]]
}

readonly require_postgres="${!REQUIRE_ENV_VAR:-}"
readonly skip_postgres="${!SKIP_ENV_VAR:-}"

if is_true "${skip_postgres}"; then
  if is_true "${require_postgres}"; then
    verify_die "${SKIP_ENV_VAR} and ${REQUIRE_ENV_VAR} are both enabled; a required Postgres tier may not be skipped"
  fi
  echo "SKIP: ${SKIP_ENV_VAR} is set; Postgres integration verifier intentionally skipped."
  exit 0
fi

if ! command -v docker >/dev/null 2>&1; then
  if is_true "${require_postgres}"; then
    verify_die "docker CLI not found while ${REQUIRE_ENV_VAR}=1"
  fi
  echo "SKIP: docker CLI not found; Postgres integration tests require Docker/Testcontainers."
  echo "      Install/start Docker or set ${SKIP_ENV_VAR}=1 for intentional skip."
  exit 0
fi

if ! docker info >/dev/null 2>&1; then
  if is_true "${require_postgres}"; then
    verify_die "Docker daemon unavailable/unhealthy while ${REQUIRE_ENV_VAR}=1"
  fi
  echo "SKIP: Docker daemon unavailable/unhealthy; cannot run Postgres integration tests."
  echo "      Start Docker, then re-run, or set ${SKIP_ENV_VAR}=1 for intentional skip."
  exit 0
fi

startup_timeout="${!TIMEOUT_ENV_VAR:-$DEFAULT_TIMEOUT_SECONDS}"
if ! [[ "${startup_timeout}" =~ ^[0-9]+$ ]] || (( startup_timeout < 15 || startup_timeout > 600 )); then
  echo "WARN: ${TIMEOUT_ENV_VAR}='${startup_timeout}' invalid; expected 15-600 seconds. Using ${DEFAULT_TIMEOUT_SECONDS}."
  startup_timeout="${DEFAULT_TIMEOUT_SECONDS}"
fi

results_dir="$(mktemp -d)"
trap 'rm -rf "${results_dir}"' EXIT
readonly trx_file="${results_dir}/postgres.trx"
readonly output_file="${results_dir}/dotnet-test.log"

declare -a timeout_prefix=()
if command -v timeout >/dev/null 2>&1; then
  timeout_prefix=(timeout "${startup_timeout}")
  echo "RUN: dotnet test --filter \"${TEST_FILTER}\" (timeout ${startup_timeout}s)"
else
  echo "RUN: dotnet test --filter \"${TEST_FILTER}\" (no timeout binary; relying on test-level timeout)"
fi

# Do not wrap this in `if ...; then`: the old implementation read `$?` after the
# `if` statement, which can erase the command's real failure status. Preserve the
# left side of the tee pipeline explicitly instead.
set +e
"${timeout_prefix[@]}" dotnet test \
  --nologo \
  --filter "${TEST_FILTER}" \
  --results-directory "${results_dir}" \
  --logger "trx;LogFileName=postgres.trx" \
  2>&1 | tee "${output_file}"
status=${PIPESTATUS[0]}
set -e

if [[ ${status} -eq 124 ]]; then
  echo "FAIL: dotnet test timed out after ${startup_timeout}s."
  echo "      Increase ${TIMEOUT_ENV_VAR} for slower machines or inspect Docker health."
  exit 124
fi
if [[ ${status} -ne 0 ]]; then
  echo "FAIL: Postgres integration tests failed (exit ${status})."
  exit "${status}"
fi

[[ -f "${trx_file}" ]] || verify_die "dotnet test succeeded but did not produce ${trx_file}"
counters="$(grep -o '<Counters[^>]*>' "${trx_file}" | head -1 || true)"
[[ -n "${counters}" ]] || verify_die "could not find TRX Counters; cannot prove the Postgres tier executed"

counter_value() {
  local name="$1"
  printf '%s\n' "${counters}" | sed -n "s/.* ${name}=\"\([0-9][0-9]*\)\".*/\1/p"
}

executed="$(counter_value executed)"
total="$(counter_value total)"
failed="$(counter_value failed)"
[[ "${executed}" =~ ^[0-9]+$ ]] || verify_die "TRX executed count missing or invalid"
[[ "${total}" =~ ^[0-9]+$ ]] || verify_die "TRX total count missing or invalid"
[[ "${failed}" =~ ^[0-9]+$ ]] || verify_die "TRX failed count missing or invalid"
(( executed > 0 )) || verify_die "Postgres filter executed zero tests"
(( total >= executed )) || verify_die "TRX counters invalid: total=${total}, executed=${executed}"
skipped=$(( total - executed ))

printf 'POSTGRES TEST SUMMARY — Executed: %d, Skipped: %d, Failed: %d, Total: %d\n' \
  "${executed}" "${skipped}" "${failed}" "${total}"
(( failed == 0 )) || verify_die "TRX reports ${failed} failed Postgres tests"
(( skipped == 0 )) || verify_die "TRX reports ${skipped} skipped/not-executed Postgres tests"

echo "PASS: Postgres integration verifier executed a non-zero, zero-skip relational tier."
