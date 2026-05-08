#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

readonly EXPECTED_SDK_VERSION="$(python3 - <<'PY'
import json
from pathlib import Path
p = Path('global.json')
if not p.exists():
    print('')
else:
    print(json.loads(p.read_text()).get('sdk', {}).get('version', ''))
PY
)"

required_failures=0
optional_warnings=0

pass() {
  printf 'PASS [required] %s\n' "$1"
}

warn() {
  printf 'WARN [optional] %s\n' "$1"
  optional_warnings=$((optional_warnings + 1))
}

fail() {
  printf 'FAIL [required] %s\n' "$1"
  required_failures=$((required_failures + 1))
}

phase() {
  printf '\n== PHASE: %s ==\n' "$1"
}

require_file() {
  local f="$1"
  if [[ -f "$f" ]]; then
    pass "file present: $f"
  else
    fail "missing required file: $f"
  fi
}

run_required_cmd() {
  local label="$1"
  shift
  if "$@"; then
    pass "$label"
  else
    fail "$label"
  fi
}

phase "tooling-preflight"
run_required_cmd "dotnet command available" bash -lc 'command -v dotnet >/dev/null 2>&1'
run_required_cmd "rg command available" bash -lc 'command -v rg >/dev/null 2>&1'
run_required_cmd "python3 command available (for JSON parsing)" bash -lc 'command -v python3 >/dev/null 2>&1'

if [[ -z "$EXPECTED_SDK_VERSION" ]]; then
  fail "global.json missing sdk.version"
else
  pass "global.json pins sdk.version=$EXPECTED_SDK_VERSION"
fi

if command -v dotnet >/dev/null 2>&1; then
  ACTUAL_SDK_VERSION="$(dotnet --version 2>/dev/null || printf '')"
  if [[ -z "$ACTUAL_SDK_VERSION" ]]; then
    fail "dotnet --version returned empty output"
  elif [[ "$ACTUAL_SDK_VERSION" == "$EXPECTED_SDK_VERSION" ]]; then
    pass "dotnet SDK matches global.json ($ACTUAL_SDK_VERSION)"
  else
    fail "dotnet SDK mismatch: expected $EXPECTED_SDK_VERSION from global.json, got $ACTUAL_SDK_VERSION"
  fi
fi

phase "docs-contract"
require_file "docs/SETUP.md"
require_file "docs/DEPLOYMENT.md"
require_file "scripts/verify/s09-package-restore-policy.sh"
require_file "scripts/verify/production-smoke.sh"

if [[ -f "docs/SETUP.md" ]]; then
  if rg -q --fixed-strings ".NET SDK/runtime contract (M003 S09)" docs/SETUP.md; then
    pass "docs/SETUP.md includes S09 SDK/runtime contract section"
  else
    fail "docs/SETUP.md missing '.NET SDK/runtime contract (M003 S09)' section"
  fi

  if rg -q --fixed-strings "Package restore reproducibility policy (M003 S09)" docs/SETUP.md; then
    pass "docs/SETUP.md includes S09 package restore policy section"
  else
    fail "docs/SETUP.md missing 'Package restore reproducibility policy (M003 S09)' section"
  fi
fi

if [[ -f "docs/DEPLOYMENT.md" ]]; then
  if rg -q --fixed-strings ".NET runtime/publish contract (M003 S09)" docs/DEPLOYMENT.md; then
    pass "docs/DEPLOYMENT.md includes S09 runtime/publish contract section"
  else
    fail "docs/DEPLOYMENT.md missing '.NET runtime/publish contract (M003 S09)' section"
  fi
fi

phase "target-framework-contract"
mapfile -t PROJECT_FILES < <(find src tests -type f \( -name '*.csproj' -o -name '*.fsproj' \) | sort)
if [[ ${#PROJECT_FILES[@]} -eq 0 ]]; then
  fail "no project files found under src/ or tests/"
else
  pass "discovered ${#PROJECT_FILES[@]} project file(s) under src/tests"
fi

non_net8_projects=()
for file in "${PROJECT_FILES[@]}"; do
  if rg -q '<TargetFramework>net8\.0</TargetFramework>' "$file"; then
    pass "target framework pinned to net8.0: $file"
  elif rg -q '<TargetFrameworks>' "$file"; then
    non_net8_projects+=("$file (uses TargetFrameworks; expected single net8.0 TargetFramework)")
  else
    non_net8_projects+=("$file (missing <TargetFramework>net8.0</TargetFramework>)")
  fi
done

if [[ ${#non_net8_projects[@]} -gt 0 ]]; then
  fail "target framework contract violation(s):"
  printf '  - %s\n' "${non_net8_projects[@]}"
fi

phase "package-policy"
if [[ ! -x "scripts/verify/s09-package-restore-policy.sh" ]]; then
  fail "scripts/verify/s09-package-restore-policy.sh is not executable"
else
  if bash scripts/verify/s09-package-restore-policy.sh; then
    pass "S09 package restore policy verifier passed"
  else
    fail "S09 package restore policy verifier failed"
  fi
fi

phase "restore-and-build"
if dotnet restore --nologo >/tmp/s09-runtime-repro-restore.log 2>&1; then
  pass "dotnet restore succeeded"
else
  fail "dotnet restore failed (see /tmp/s09-runtime-repro-restore.log)"
  tail -n 80 /tmp/s09-runtime-repro-restore.log
fi

if dotnet build --nologo --no-restore >/tmp/s09-runtime-repro-build.log 2>&1; then
  pass "dotnet build --no-restore succeeded"
else
  fail "dotnet build --no-restore failed (see /tmp/s09-runtime-repro-build.log)"
  tail -n 120 /tmp/s09-runtime-repro-build.log
fi

phase "runtime-preflight-token-contract"
if [[ -f "scripts/verify/production-smoke.sh" ]]; then
  if rg -q --fixed-strings "phase \"runtime-preflight\"" scripts/verify/production-smoke.sh; then
    pass "production-smoke includes runtime-preflight phase token"
  else
    fail "production-smoke missing runtime-preflight phase token"
  fi

  if rg -q --fixed-strings "check_runtime_preflight" scripts/verify/production-smoke.sh; then
    pass "production-smoke includes runtime preflight check function call"
  else
    fail "production-smoke missing runtime preflight check function call"
  fi

  if rg -q --fixed-strings "SMOKE_EXPECT_RUNTIME" scripts/verify/production-smoke.sh; then
    pass "production-smoke declares SMOKE_EXPECT_RUNTIME contract token"
  else
    fail "production-smoke missing SMOKE_EXPECT_RUNTIME contract token"
  fi

  if rg -q --fixed-strings "SMOKE_EXPECT_SELF_CONTAINED" scripts/verify/production-smoke.sh; then
    pass "production-smoke declares SMOKE_EXPECT_SELF_CONTAINED contract token"
  else
    fail "production-smoke missing SMOKE_EXPECT_SELF_CONTAINED contract token"
  fi
fi

phase "optional-environment-signals"
if command -v docker >/dev/null 2>&1; then
  pass "docker command present (optional runtime context)"
else
  warn "docker command missing; optional for this local reproducibility verifier"
fi

if command -v systemctl >/dev/null 2>&1; then
  pass "systemctl command present (optional runtime context)"
else
  warn "systemctl missing; expected on non-systemd local environments"
fi

phase "summary"
printf 'RESULT required_failures=%s optional_warnings=%s\n' "$required_failures" "$optional_warnings"

if (( required_failures > 0 )); then
  exit 1
fi

exit 0
