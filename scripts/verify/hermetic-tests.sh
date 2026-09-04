#!/usr/bin/env bash
set -euo pipefail

# hermetic-tests.sh — run the non-Postgres suite with developer-machine
# configuration stripped out, so a clean clone and a workstation agree.
#
# WHY THIS EXISTS
#
# WarHistoryIngestWriterTests passed here and failed in CI. The test injected
# ConnectionStrings:Default, a key the code never reads — and it passed anyway,
# because this machine sets
#
#     ~/.config/environment.d/happygymstats.conf
#       ConnectionStrings__HappyGymStats=...
#
# for the whole systemd user session. Host.CreateApplicationBuilder reads
# unprefixed environment variables and maps "__" to ":", so every process on the
# machine — dotnet test included — was handed the real connection string. The
# test was wrong and the environment covered for it. A clean runner had nothing
# to cover with.
#
# The variable is legitimate; it is how the local WarPoller is configured. What
# is not legitimate is a test suite that silently consumes it.
#
# WHAT THIS DOES NOT DO
#
# It does not run the Postgres tier. Those tests need real infrastructure by
# design and are gated separately (#60) with
# HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION, so that a skip there is a hard
# failure rather than a silent pass. Excluding them here is the point: this
# script answers "does the hermetic suite stand on its own", and nothing else.

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands dotnet

# Configuration a test must never inherit. Extend this list rather than teaching
# a test to tolerate ambient values.
#
# ConnectionStrings__* and *__* generally: ASP.NET's environment-variable
# provider turns a double underscore into a configuration-section separator, so
# any such variable is live configuration to Host.CreateApplicationBuilder.
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

# Anything section-shaped that we did not name explicitly. Caught rather than
# assumed, because the failure mode is a variable nobody thought to list.
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

# The second ambient source, and the one stripping variables cannot reach: each
# referenced host ships an appsettings.json, exactly one can land in the output
# directory, and which one wins is build-order dependent. A test host reading
# AppContext.BaseDirectory therefore got the API's real ConnectionStrings here
# and something else on a runner. The test csproj deletes them after build; this
# asserts that stayed true, because the failure is silent when it does not.
# Check the directory the run will actually load from, found via the newest test
# assembly. Scanning all of bin/ would flag stale output from an older TFM or a
# previous Release build — files nothing loads, which would make this guard cry
# wolf and get switched off.
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

echo "==> dotnet test (${filter})"
env "${unset_args[@]}" dotnet test --nologo --filter "${filter}" "$@"

echo "PASS: the hermetic suite passes with developer configuration removed"
