#!/usr/bin/env bash
set -euo pipefail

# hermetic-tests.sh — run the non-Postgres suite with developer-machine
# configuration stripped out, from a working directory outside the repository.
#
# WHY THIS EXISTS
#
# WarHistoryIngestWriterTests passed here and failed in CI. The test injected
# ConnectionStrings:Default, a key the code never reads — and it passed anyway,
# because this machine sets ConnectionStrings__HappyGymStats for the whole user
# session. Host.CreateApplicationBuilder reads unprefixed environment variables
# and maps "__" to ":", so dotnet test inherited the real connection string.
#
# A second ambient source came from referenced hosts copying competing
# appsettings.json files into the test output. The test project now removes those
# files after build and this runner asserts they stayed gone.
#
# The third ambient assumption is the caller's current working directory. Tests
# must find deliberate repository fixtures from stable test/repository context,
# not because `dotnet test` happened to be launched from the checkout root. This
# runner therefore executes the test project by absolute path from a fresh temp
# directory. A cwd-sensitive test fails here instead of working by accident.
#
# WHAT THIS DOES NOT DO
#
# It does not run the Postgres tier. Those tests need real infrastructure by
# design and are gated separately (#60) with
# HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION so a skip becomes a hard failure.

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands dotnet env find sort head cut sed mktemp
verify_require_file "${TEST_PROJECT}"

# Configuration a test must never inherit. Extend this list rather than teaching
# a test to tolerate ambient values.
readonly -a STRIPPED=(
  ConnectionStrings__HappyGymStats
  HAPPYGYMSTATS_CONNECTION_STRING
  HAPPYGYMSTATS_DATA_DIR
  HAPPYGYMSTATS_DATABASE
  HAPPYGYMSTATS_DEV_AUTH
  HAPPYGYMSTATS_DEV_SKIP_WAR_SEED
  DOTNET_ENVIRONMENT
  ASPNETCORE_ENVIRONMENT
  ASPNETCORE_URLS
  ApiBaseUrl
)

# Anything section-shaped that we did not name explicitly. ASP.NET's
# environment-variable provider maps a double underscore to a configuration
# section separator, so a forgotten Foo__Bar value is ambient configuration too.
mapfile -t discovered < <(env | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*__[A-Za-z0-9_]*\)=.*/\1/p' || true)

declare -a to_strip=("${STRIPPED[@]}")
for name in "${discovered[@]:-}"; do
  [[ -z "${name}" ]] && continue
  [[ " ${to_strip[*]} " == *" ${name} "* ]] || to_strip+=("${name}")
done

echo "==> hermetic run: stripping developer configuration"
declare -a unset_args=()
for name in "${to_strip[@]}"; do
  if [[ -n "${!name-}" ]]; then
    printf '    unset %s\n' "${name}"
  fi
  unset_args+=(-u "${name}")
done

# HERMETIC_EXTRA_FILTER narrows the run without colliding with the category
# exclusion; passing a second --filter to dotnet test silently overrides the
# first, which would quietly re-admit the Postgres tier.
filter="Category!=PostgresApiIntegration"
if [[ -n "${HERMETIC_EXTRA_FILTER:-}" ]]; then
  filter="${filter}&${HERMETIC_EXTRA_FILTER}"
fi

# Assert referenced hosts did not leak an appsettings file into the output the
# test run will actually load from. Scan the newest test assembly's directory,
# not all of bin/, so stale output from another TFM/configuration cannot cry wolf.
test_dll="$(find tests/HappyGymStats.Tests/bin -name 'HappyGymStats.Tests.dll' -newer tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj -printf '%T@ %p\n' 2>/dev/null \
  | sort -rn | head -1 | cut -d' ' -f2- || true)"
if [[ -n "${test_dll}" ]]; then
  out_dir="$(dirname "${test_dll}")"
  leaked="$(find "${out_dir}" -maxdepth 1 -name 'appsettings*.json' -print 2>/dev/null || true)"
  if [[ -n "${leaked}" ]]; then
    printf '%s\n' "${leaked}" >&2
    verify_die "host appsettings reached ${out_dir} — tests can silently consume it (see RemoveInheritedHostAppSettings in the test csproj)"
  fi
fi
echo "    no host appsettings in the test output"

# Deliberately leave the checkout before launching the testhost. Tests receive an
# absolute project path, so build inputs remain well-defined while
# Directory.GetCurrentDirectory() points somewhere with no solution, fixtures,
# appsettings, or developer files to discover by accident.
readonly HERMETIC_CWD="$(mktemp -d)"
cleanup() {
  rm -rf "${HERMETIC_CWD}"
}
trap cleanup EXIT
cd "${HERMETIC_CWD}" || verify_die "cannot cd to hermetic working directory"
printf '    working directory: outside repository (%s)\n' "${HERMETIC_CWD}"

echo "==> dotnet test (${filter})"
env "${unset_args[@]}" dotnet test "${TEST_PROJECT}" --nologo --filter "${filter}" "$@"

echo "PASS: the hermetic suite passes without developer configuration, host appsettings, or repository cwd"
