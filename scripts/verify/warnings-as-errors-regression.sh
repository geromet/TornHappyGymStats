#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "${BASH_SOURCE[0]%/*}/../.." && pwd)"
TMP_DIR="$(mktemp -d "${ROOT_DIR}/.warning-gate.XXXXXX")"
trap 'rm -rf "${TMP_DIR}"' EXIT

cat > "${TMP_DIR}/WarningGate.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
EOF

cat > "${TMP_DIR}/Program.cs" <<'EOF'
int deliberatelyUnused = 42;
Console.WriteLine("warning gate fixture");
EOF

set +e
output="$(dotnet build "${TMP_DIR}/WarningGate.csproj" --nologo 2>&1)"
status=$?
set -e

if (( status == 0 )); then
  printf '%s\n' "${output}"
  echo "FAIL: a compiler warning did not fail the build"
  exit 1
fi

if ! grep -Eq 'CS0219.*error|error CS0219' <<< "${output}"; then
  printf '%s\n' "${output}"
  echo "FAIL: fixture failed, but not because CS0219 was promoted to an error"
  exit 1
fi

echo "PASS: compiler warnings are promoted to build errors by shared policy"
