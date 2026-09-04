#!/usr/bin/env bash
# package-version-policy.sh — package versions live in Directory.Packages.props.
#
# Central Package Management is only central while nothing re-pins a version in a
# project file. A stray Version= still builds, so nothing would notice until two
# projects disagree about a package again — which is how AdminPanel ended up
# loading EF Relational 10.0.4 while everything else compiled against 10.0.11.
#
# VersionOverride is allowed: it is CPM's supported escape hatch and it states the
# divergence out loud in the project that needs it, rather than hiding a second
# source of truth.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=scripts/verify/verify-common.sh
source "${SCRIPT_DIR}/verify-common.sh"
cd "${ROOT_DIR}" || verify_die "cannot cd to ${ROOT_DIR}"

verify_require_commands rg
verify_require_file Directory.Packages.props

echo "==> Checking package versions are centrally managed"

rg -q '<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>' Directory.Packages.props \
  || verify_die "Directory.Packages.props does not enable ManagePackageVersionsCentrally"

count="$(verify_require_files_matching . '*.csproj')"

# A PackageReference carrying a literal Version=. VersionOverride= is excluded by
# requiring the attribute name to start immediately after a space.
verify_no_match \
  "a project file pins a package version; move it to Directory.Packages.props (or use VersionOverride= with a reason)" \
  --glob '*.csproj' --glob '!**/bin/**' --glob '!**/obj/**' \
  '<PackageReference[^>]*\sVersion="' \
  .

echo "PASS: no project file pins a package version (${count} .csproj scanned)"
