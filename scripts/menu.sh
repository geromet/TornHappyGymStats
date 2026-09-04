#!/usr/bin/env bash
# menu.sh — the operator console for this repository.
#
# One place to run everything that touches the server, so nothing has to be
# reconstructed from a chat log or a runbook. It does not reimplement any
# script: it supplies the arguments and the environment gates the scripts
# require, shows the exact command before running it, and never applies
# anything without an explicit confirmation typed in the menu.
#
# The scripts keep their own gates. Running one directly still needs its
# DEPLOY_*=1 and its --confirm-* flag; the menu is a convenience, not a bypass.
#
#   bash scripts/menu.sh                 interactive
#   bash scripts/menu.sh --list          every task, with its id
#   bash scripts/menu.sh --run <id>      one task, preview only
#   bash scripts/menu.sh --run <id> --apply
#   bash scripts/menu.sh --audit         scripts no menu entry drives
#   bash scripts/menu.sh --pitfalls      the operational pitfalls file
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PITFALLS_DOC="${ROOT_DIR}/docs/OPERATIONS-PITFALLS.md"

# shellcheck source=lib/ui.sh
source "${SCRIPT_DIR}/lib/ui.sh"
# shellcheck source=lib/registry.sh
source "${SCRIPT_DIR}/lib/registry.sh"

# ── Task execution ───────────────────────────────────────────────────────────

# Builds the command line for one entry in one mode, into the array MENU_CMD.
build_command() {
  local record="$1" mode="$2"
  local script args env_pairs
  script="$(reg_field "${record}" 4)"
  MENU_CMD=()

  if [[ "${mode}" == "apply" ]]; then
    args="$(reg_field "${record}" 6)"
    env_pairs="$(reg_field "${record}" 7)"
    if [[ -n "${env_pairs}" ]]; then
      # shellcheck disable=SC2206  # deliberate split; see the note below.
      MENU_CMD+=(env ${env_pairs})
    fi
  else
    args="$(reg_field "${record}" 5)"
  fi

  MENU_CMD+=(bash "${SCRIPT_DIR}/${script}")
  # Unquoted on purpose: these are fixed flag strings from this repository's own
  # registry, never operator input, and they must split into separate arguments.
  # shellcheck disable=SC2206  # the split is the point, per the note above.
  [[ "${args}" != "-" && -n "${args}" ]] && MENU_CMD+=(${args})
  return 0
}

run_task() {
  local record="$1" mode="$2"
  local id label script preview apply caution
  id="$(reg_field "${record}" 1)"
  label="$(reg_field "${record}" 3)"
  script="$(reg_field "${record}" 4)"
  preview="$(reg_field "${record}" 5)"
  apply="$(reg_field "${record}" 6)"
  caution="$(reg_field "${record}" 9)"

  if [[ ! -f "${SCRIPT_DIR}/${script}" ]]; then
    ui_error "Registry entry '${id}' points at scripts/${script}, which does not exist."
    return 1
  fi

  if [[ "${mode}" == "preview" && "${preview}" == "NONE" ]]; then
    ui_warn "'${label}' has no safe preview — it either does the thing or it does not."
    return 1
  fi

  if [[ "${mode}" == "apply" ]]; then
    if [[ "${apply}" == "NONE" ]]; then
      ui_warn "'${label}' is read-only. There is nothing to apply."
      return 1
    fi

    if [[ "${caution}" != "-" ]]; then
      ui_caution "Before you do this" "${caution}"
    fi

    build_command "${record}" apply
    ui_show_command "${MENU_CMD[*]}"
    ui_blank
    if ! ui_confirm_phrase "APPLY"; then
      ui_info "Cancelled. Nothing ran."
      return 0
    fi
  else
    build_command "${record}" preview
  fi

  ui_blank
  ui_run "${MENU_CMD[@]}" || true
}

# ── Interactive ──────────────────────────────────────────────────────────────

task_screen() {
  local record="$1"
  local label blurb preview apply
  label="$(reg_field "${record}" 3)"
  blurb="$(reg_field "${record}" 8)"
  preview="$(reg_field "${record}" 5)"
  apply="$(reg_field "${record}" 6)"

  while true; do
    ui_title "${label}"
    ui_info "${blurb}"
    ui_dim "scripts/$(reg_field "${record}" 4)   ·   id: $(reg_field "${record}" 1)"

    local -a options=()
    [[ "${preview}" != "NONE" ]] && options+=("Preview — show what this would do, change nothing")
    [[ "${apply}" != "NONE" ]] && options+=("Apply — make the change")

    if (( ${#options[@]} == 0 )); then
      ui_error "This entry has neither a preview nor an apply mode; the registry is wrong."
      ui_pause
      return 0
    fi

    ui_menu "Options" "${options[@]}"
    (( UI_CHOICE == 0 )) && return 0

    local chosen="${options[$((UI_CHOICE - 1))]}"
    case "${chosen}" in
      Preview*) run_task "${record}" preview ;;
      Apply*)   run_task "${record}" apply ;;
    esac
    ui_pause
  done
}

category_screen() {
  local category="$1"
  while true; do
    local -a labels=() records=()
    local record
    for record in "${REG_ENTRIES[@]}"; do
      if [[ "$(reg_field "${record}" 2)" == "${category}" ]]; then
        labels+=("$(reg_field "${record}" 3)")
        records+=("${record}")
      fi
    done

    ui_menu "${category}" "${labels[@]}"
    (( UI_CHOICE == 0 )) && return 0
    task_screen "${records[$((UI_CHOICE - 1))]}"
  done
}

main_menu() {
  local -a categories=()
  mapfile -t categories < <(reg_categories)

  while true; do
    local -a items=("${categories[@]}" "Operational pitfalls — what has bitten us, and how to notice" "Audit — scripts this menu does not cover")

    ui_menu "HappyGymStats — operator console" "${items[@]}"
    if (( UI_CHOICE == 0 )); then
      ui_blank
      ui_info "Bye."
      return 0
    fi

    local index=$((UI_CHOICE - 1))
    if (( index < ${#categories[@]} )); then
      category_screen "${categories[$index]}"
    elif (( index == ${#categories[@]} )); then
      show_pitfalls
    else
      audit
      ui_pause
    fi
  done
}

# ── Non-interactive entry points ─────────────────────────────────────────────

list_tasks() {
  local record category last=""
  ui_title "Tasks"
  for record in "${REG_ENTRIES[@]}"; do
    category="$(reg_field "${record}" 2)"
    if [[ "${category}" != "${last}" ]]; then
      printf '\n%s%s%s\n' "${UI_BOLD}" "${category}" "${UI_RESET}"
      last="${category}"
    fi
    printf '  %-22s %s\n' "$(reg_field "${record}" 1)" "$(reg_field "${record}" 3)"
  done
  ui_blank
  ui_dim "bash scripts/menu.sh --run <id>            preview"
  ui_dim "bash scripts/menu.sh --run <id> --apply    make the change"
}

audit() {
  ui_title "Coverage audit"

  local -A covered=() excluded_reason=()
  local record entry name reason script
  for record in "${REG_ENTRIES[@]}"; do
    script="$(reg_field "${record}" 4)"
    covered["${script}"]=1
  done
  for entry in "${REG_EXCLUDED[@]}"; do
    name="${entry%%:*}"
    reason="${entry#*:}"
    excluded_reason["${name}"]="${reason}"
  done

  local -a uncovered=() missing=()
  local file base
  for file in "${SCRIPT_DIR}"/*.sh; do
    base="$(basename "${file}")"
    [[ -n "${covered[${base}]:-}" ]] && continue
    [[ -n "${excluded_reason[${base}]:-}" ]] && continue
    uncovered+=("${base}")
  done

  for record in "${REG_ENTRIES[@]}"; do
    script="$(reg_field "${record}" 4)"
    [[ -f "${SCRIPT_DIR}/${script}" ]] || missing+=("$(reg_field "${record}" 1) -> scripts/${script}")
  done

  if (( ${#missing[@]} > 0 )); then
    ui_error "Registry entries pointing at scripts that do not exist:"
    printf '   %s\n' "${missing[@]}"
    ui_blank
  fi

  if (( ${#uncovered[@]} > 0 )); then
    ui_warn "In scripts/ but not in the menu and not listed as excluded:"
    printf '   %s\n' "${uncovered[@]}"
    ui_blank
    ui_dim "Add an entry in scripts/lib/registry.sh, or add it to REG_EXCLUDED with a reason."
  else
    ui_ok "Every script in scripts/ is either driven by the menu or excluded with a reason."
  fi

  ui_blank
  ui_info "${UI_BOLD}Deliberately excluded${UI_RESET}"
  for entry in "${REG_EXCLUDED[@]}"; do
    printf '   %-34s %s\n' "${entry%%:*}" "${entry#*:}"
  done

  (( ${#missing[@]} == 0 && ${#uncovered[@]} == 0 ))
}

show_pitfalls() {
  if [[ ! -f "${PITFALLS_DOC}" ]]; then
    ui_error "Missing ${PITFALLS_DOC#"${ROOT_DIR}/"}"
    ui_pause
    return 1
  fi
  if command -v less >/dev/null 2>&1 && [[ -t 1 ]]; then
    less -R "${PITFALLS_DOC}"
  else
    cat "${PITFALLS_DOC}"
  fi
}

usage() {
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# ── Argument handling ────────────────────────────────────────────────────────

MODE="interactive"
TASK_ID=""
APPLY=0

while (( $# > 0 )); do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --list) MODE="list"; shift ;;
    --audit) MODE="audit"; shift ;;
    --pitfalls) MODE="pitfalls"; shift ;;
    --run) MODE="run"; TASK_ID="${2:-}"; shift 2 ;;
    --apply) APPLY=1; shift ;;
    *) ui_error "Unknown option '$1'. Try --help."; exit 2 ;;
  esac
done

case "${MODE}" in
  list) list_tasks ;;
  audit) audit ;;
  pitfalls) show_pitfalls ;;
  run)
    if [[ -z "${TASK_ID}" ]]; then
      ui_error "--run needs a task id. See --list."
      exit 2
    fi
    record="$(reg_find "${TASK_ID}")" || { ui_error "No task '${TASK_ID}'. See --list."; exit 2; }
    if (( APPLY )); then
      run_task "${record}" apply
    else
      run_task "${record}" preview
    fi
    ;;
  interactive)
    if [[ ! -t 0 ]]; then
      ui_error "The menu needs a terminal. For scripts use --run <id> [--apply]."
      exit 2
    fi
    main_menu
    ;;
esac
