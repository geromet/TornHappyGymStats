#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

echo "[s09] package restore policy verifier"

SETUP_DOC="docs/SETUP.md"
if [[ ! -f "$SETUP_DOC" ]]; then
  echo "[s09][fail] missing docs/SETUP.md"
  exit 1
fi

# Floating/range packages are blocked by default. If one is intentionally required,
# add an entry here and add matching justification text in docs/SETUP.md.
# Format: "PackageName|justification phrase expected in docs"
ALLOWED_FLOATING=()

echo "[s09] scanning tracked csproj files"
mapfile -t CSPROJ_FILES < <(find src tests -type f -name '*.csproj' | sort)

if [[ ${#CSPROJ_FILES[@]} -eq 0 ]]; then
  echo "[s09][fail] no csproj files found under src/ or tests/"
  exit 1
fi

# Match floating/ranged versions like 8.*, [8.0,9.0), (1.0,2.0], 8.0.*
FLOATING_PATTERN='Version="[^"]*(\*|\[|\]|\(|\)|,)[^"]*"'

floating_found=0
while IFS= read -r match; do
  floating_found=1
  file="${match%%:*}"
  line_rest="${match#*:}"
  line_no="${line_rest%%:*}"
  content="${line_rest#*:}"

  pkg_name="$(sed -E 's/.*Include="([^"]+)".*/\1/' <<<"$content")"
  if [[ "$pkg_name" == "$content" ]]; then
    pkg_name="<unknown>"
  fi

  allowed=0
  for entry in "${ALLOWED_FLOATING[@]}"; do
    allow_pkg="${entry%%|*}"
    justification="${entry#*|}"
    if [[ "$pkg_name" == "$allow_pkg" ]]; then
      if rg -q --fixed-strings "$justification" "$SETUP_DOC"; then
        allowed=1
      else
        echo "[s09][fail] floating package '$pkg_name' found at ${file}:${line_no} but docs justification missing phrase: $justification"
        exit 1
      fi
      break
    fi
  done

  if [[ $allowed -eq 0 ]]; then
    echo "[s09][fail] floating/ranged package version not allowlisted: ${file}:${line_no}"
    echo "  -> $content"
    echo "  Add explicit pin, or add allowlist + justification in docs/SETUP.md and this script."
    exit 1
  fi

done < <(rg -n "$FLOATING_PATTERN" "${CSPROJ_FILES[@]}" || true)

if [[ $floating_found -eq 0 ]]; then
  echo "[s09][pass] no floating/ranged package versions detected"
else
  echo "[s09][pass] floating/ranged packages are allowlisted with matching docs justification"
fi

echo "[s09] validating lockfile policy"
if ! rg -q --fixed-strings "Lockfile decision:" "$SETUP_DOC"; then
  echo "[s09][fail] docs/SETUP.md missing 'Lockfile decision' policy declaration"
  exit 1
fi

mapfile -t LOCKFILES < <(find src tests -type f -name 'packages.lock.json' | sort)
if [[ ${#LOCKFILES[@]} -gt 0 ]]; then
  echo "[s09][fail] lockfiles present but policy currently expects none"
  printf '  - %s\n' "${LOCKFILES[@]}"
  exit 1
fi
echo "[s09][pass] no packages.lock.json files present (matches documented policy)"

echo "[s09] running restore"
dotnet restore --nologo >/tmp/s09-restore.log 2>&1 || {
  echo "[s09][fail] dotnet restore failed"
  tail -n 120 /tmp/s09-restore.log || true
  exit 1
}

echo "[s09][pass] dotnet restore succeeded"
echo "[s09][pass] package restore policy verified"
