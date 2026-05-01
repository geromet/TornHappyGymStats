#!/usr/bin/env bash
# menu.sh — Interactive launcher for run/publish/deploy/verify flows.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

help_main() {
  cat <<EOF
Usage:
  bash scripts/menu.sh                 Start interactive menu
  bash scripts/menu.sh --help         Show this help

What this menu runs:
  - Run CLI (scripts/run-cli.sh)
  - Publish artifacts (scripts/publish.sh)
  - Deploy backend (scripts/deploy-backend.sh)
  - Deploy frontend (scripts/deploy-frontend.sh)
  - Verify scripts (scripts/verify/*)

Tips:
  - Use Publish -> all for both CLI and API outputs.
  - Use Deploy Frontend -> trigger to manually dispatch Pages workflow.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  help_main
  exit 0
fi

run_cli() { bash "${SCRIPT_DIR}/run-cli.sh"; }

publish_menu() {
  echo "Publish target (cli/api/all):"
  select target in "cli" "api" "all" "back"; do
    case "$target" in
      cli|api|all)
        read -r -p "RIDs (comma-separated, blank=defaults): " rid_input
        read -r -p "Configuration [Release]: " config
        config="${config:-Release}"
        read -r -p "Framework-dependent publish? [y/N]: " fd

        args=(--target "$target" --configuration "$config")
        if [[ -n "${rid_input}" ]]; then
          IFS=',' read -r -a rid_list <<< "${rid_input}"
          for rid in "${rid_list[@]}"; do
            trimmed="$(echo "$rid" | xargs)"
            [[ -n "$trimmed" ]] && args+=(--rid "$trimmed")
          done
        fi

        if [[ "${fd}" =~ ^[Yy]$ ]]; then
          args+=(--framework-dependent)
        fi

        bash "${SCRIPT_DIR}/publish.sh" "${args[@]}"
        break ;;
      back) break ;;
      *) echo "Invalid option" ;;
    esac
  done
}

deploy_frontend_menu() {
  echo "Frontend deploy mode:"
  select mode in "validate" "trigger" "back"; do
    case "$mode" in
      validate|trigger)
        bash "${SCRIPT_DIR}/deploy-frontend.sh" --mode "$mode"
        break ;;
      back) break ;;
      *) echo "Invalid option" ;;
    esac
  done
}

verify_menu() {
  echo "Verification script:"
  select v in "build-and-test" "publish-smoke" "back"; do
    case "$v" in
      build-and-test) bash "${SCRIPT_DIR}/verify/build-and-test.sh"; break ;;
      publish-smoke) bash "${SCRIPT_DIR}/verify/publish-smoke.sh"; break ;;
      back) break ;;
      *) echo "Invalid option" ;;
    esac
  done
}

while true; do
  echo
  echo "HappyGymStats menu"
  select choice in "Run CLI" "Publish" "Deploy Backend" "Deploy Frontend" "Verify" "Help" "Exit"; do
    case "$choice" in
      "Run CLI") run_cli; break ;;
      "Publish") publish_menu; break ;;
      "Deploy Backend") bash "${SCRIPT_DIR}/deploy-backend.sh"; break ;;
      "Deploy Frontend") deploy_frontend_menu; break ;;
      "Verify") verify_menu; break ;;
      "Help") help_main; break ;;
      "Exit") exit 0 ;;
      *) echo "Invalid option" ;;
    esac
  done
done
