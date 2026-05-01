#!/usr/bin/env bash
# publish.sh — Publish CLI and/or API for one or more runtime identifiers.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly CLI_PROJECT="${ROOT_DIR}/src/HappyGymStats.Cli/HappyGymStats.Cli.csproj"
readonly API_PROJECT="${ROOT_DIR}/src/HappyGymStats.Api/HappyGymStats.Api.csproj"
readonly DEFAULT_RIDS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)

TARGET="cli"
RIDS=()
CONFIGURATION="Release"
SELF_CONTAINED="true"
SINGLE_FILE="true"

usage() {
  cat <<EOF
Usage: bash scripts/publish.sh [options]

Options:
  --target <cli|api|all>     What to publish (default: cli)
  --rid <RID>                Runtime identifier (repeatable). Default: all common RIDs.
  --configuration <Config>   Build config (default: Release)
  --framework-dependent      Publish framework-dependent binaries
  -h, --help                 Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target)
      TARGET="$2"; shift 2 ;;
    --rid)
      RIDS+=("$2"); shift 2 ;;
    --configuration)
      CONFIGURATION="$2"; shift 2 ;;
    --framework-dependent)
      SELF_CONTAINED="false"; SINGLE_FILE="false"; shift ;;
    -h|--help)
      usage; exit 0 ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1 ;;
  esac
done

if [[ ${#RIDS[@]} -eq 0 ]]; then
  RIDS=("${DEFAULT_RIDS[@]}")
fi

publish_project() {
  local project="$1"
  local name="$2"
  local base_out="$3"

  for rid in "${RIDS[@]}"; do
    local out_dir="${ROOT_DIR}/dist/${base_out}/${rid}"
    echo "==> [${name}:${rid}] dotnet publish"
    dotnet publish "${project}" \
      -r "${rid}" \
      --self-contained "${SELF_CONTAINED}" \
      -p:PublishSingleFile="${SINGLE_FILE}" \
      -c "${CONFIGURATION}" \
      -o "${out_dir}" \
      /warnaserror
    echo "    ✓ ${out_dir}"
  done
}

case "${TARGET}" in
  cli)
    publish_project "${CLI_PROJECT}" "cli" "cli"
    ;;
  api)
    publish_project "${API_PROJECT}" "api" "api"
    ;;
  all)
    publish_project "${CLI_PROJECT}" "cli" "cli"
    publish_project "${API_PROJECT}" "api" "api"
    ;;
  *)
    echo "Invalid --target: ${TARGET}. Use cli|api|all." >&2
    exit 1
    ;;
esac

echo "==> Publish completed"
