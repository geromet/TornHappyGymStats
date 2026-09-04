#!/usr/bin/env bash
set -euo pipefail

# Sabotage proof for #57: plant exactly the forbidden structured-log template
# that the privacy verifier exists to reject, then prove it fails for that
# reason. The temporary source file is always removed before returning.
readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${ROOT_DIR}/scripts/verify/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands bash rg wc grep rm

readonly sabotage_file="src/HappyGymStats.Core/PrivacyVerifierSabotage.cs"
[[ ! -e "${sabotage_file}" ]] || verify_die "refusing to overwrite existing ${sabotage_file}"
cleanup() {
  rm -f "${sabotage_file}"
}
trap cleanup EXIT

cat > "${sabotage_file}" <<'EOF'
namespace HappyGymStats.Core;

internal static class PrivacyVerifierSabotage
{
    public static void Emit(dynamic logger, long tornPlayerId) =>
        logger.LogInformation("Torn player {TornPlayerId}", tornPlayerId);
}
EOF

status=0
output="$(bash scripts/verify/no-raw-playerid-log-templates.sh 2>&1)" || status=$?

(( status == 1 )) || verify_die "privacy sabotage should be a real finding (exit 1), got ${status}. Output: ${output}"
printf '%s\n' "${output}" | grep -Fq 'FAIL: raw player-id log template detected' \
  || verify_die "privacy verifier failed for the wrong reason. Output: ${output}"
if printf '%s\n' "${output}" | grep -Fq 'PASS:'; then
  verify_die "privacy verifier printed PASS while the forbidden template existed. Output: ${output}"
fi

echo "PASS: privacy verifier rejects a planted raw-player-id log template for the intended reason"
