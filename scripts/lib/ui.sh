#!/usr/bin/env bash
# ui.sh — terminal presentation helpers shared by scripts/menu.sh.
# shellcheck shell=bash
# This file is intended to be sourced.
#
# Deliberately presentation only: no ssh, no deploy logic, no knowledge of any
# particular script. Anything that talks to the server belongs in
# lib/remote-exec.sh or in the script being driven.

[[ -n "${_HGS_UI_LOADED:-}" ]] && return 0
readonly _HGS_UI_LOADED=1

# Colour only when stdout is a terminal that wants it. Piping the menu into a
# file or a pager should not produce escape soup, and NO_COLOR is honoured
# because operators who set it mean it.
if [[ -t 1 && -z "${NO_COLOR:-}" && "${TERM:-dumb}" != "dumb" ]]; then
  readonly UI_BOLD=$'\033[1m'
  readonly UI_DIM=$'\033[2m'
  readonly UI_RED=$'\033[31m'
  readonly UI_GREEN=$'\033[32m'
  readonly UI_YELLOW=$'\033[33m'
  readonly UI_BLUE=$'\033[36m'
  readonly UI_RESET=$'\033[0m'
else
  readonly UI_BOLD='' UI_DIM='' UI_RED='' UI_GREEN='' UI_YELLOW='' UI_BLUE='' UI_RESET=''
fi

ui_rule() { printf '%s%s%s\n' "${UI_DIM}" "$(printf '─%.0s' $(seq 1 "${1:-64}"))" "${UI_RESET}"; }

ui_title() {
  printf '\n%s%s%s\n' "${UI_BOLD}" "$1" "${UI_RESET}"
  ui_rule "${#1}"
}

ui_info()  { printf '%s\n' "$*"; }
ui_dim()   { printf '%s%s%s\n' "${UI_DIM}" "$*" "${UI_RESET}"; }
ui_ok()    { printf '%s✓%s %s\n' "${UI_GREEN}" "${UI_RESET}" "$*"; }
ui_warn()  { printf '%s!%s %s\n' "${UI_YELLOW}" "${UI_RESET}" "$*"; }
ui_error() { printf '%s✗%s %s\n' "${UI_RED}" "${UI_RESET}" "$*" >&2; }
ui_blank() { printf '\n'; }

# A boxed caution. Used before anything that changes the server, so the warning
# does not scroll past as one more grey line.
ui_caution() {
  printf '\n%s%s  %s  %s\n' "${UI_YELLOW}" "${UI_BOLD}" "$1" "${UI_RESET}"
  shift
  local line
  for line in "$@"; do
    printf '   %s\n' "${line}"
  done
}

# Prints the exact command about to run. The point of the menu is that the
# operator never types these, NOT that they never see them: an operator who can
# read the command can also reproduce it, and can tell when it is wrong before
# it runs rather than afterwards.
ui_show_command() {
  printf '\n%sWill run:%s\n' "${UI_BOLD}" "${UI_RESET}"
  printf '   %s%s%s\n' "${UI_BLUE}" "$*" "${UI_RESET}"
}

# Reads one line, tolerating EOF (^D) as "no answer" rather than looping forever
# on a closed stdin — which is what turns a piped menu into a spin.
ui_read() {
  local __prompt="$1" __var="$2" __default="${3:-}" __reply
  if ! IFS= read -r -p "${__prompt}" __reply; then
    printf '\n'
    __reply=""
  fi
  [[ -z "${__reply}" ]] && __reply="${__default}"
  printf -v "${__var}" '%s' "${__reply}"
}

# Yes/no, defaulting to NO. Every caller here is about to change something.
ui_confirm() {
  local reply
  ui_read "$1 [y/N]: " reply "n"
  [[ "${reply}" =~ ^[Yy]([Ee][Ss])?$ ]]
}

# Type-the-word confirmation for the genuinely irreversible. A y/N is too easy
# to hit by reflex when the previous six prompts were also y/N.
ui_confirm_phrase() {
  local phrase="$1" reply
  ui_read "Type ${UI_BOLD}${phrase}${UI_RESET} to proceed (anything else cancels): " reply ""
  [[ "${reply}" == "${phrase}" ]]
}

# Menu of numbered choices. Returns the chosen index in UI_CHOICE (1-based), or
# 0 for the back/quit entry. Rejects out-of-range input instead of falling
# through, because `select` silently re-prompting is how people end up running
# entry 3 when they meant 13.
ui_menu() {
  local title="$1"; shift
  local -a items=("$@")
  local i reply

  ui_title "${title}"
  for i in "${!items[@]}"; do
    printf '  %s%2d%s  %s\n' "${UI_BOLD}" "$((i + 1))" "${UI_RESET}" "${items[$i]}"
  done
  printf '   %s0%s  back\n' "${UI_BOLD}" "${UI_RESET}"
  ui_blank

  while true; do
    ui_read "Choice: " reply ""
    if [[ "${reply}" == "0" || "${reply}" == "b" || "${reply}" == "q" ]]; then
      UI_CHOICE=0
      return 0
    fi
    if [[ "${reply}" =~ ^[0-9]+$ ]] && (( reply >= 1 && reply <= ${#items[@]} )); then
      UI_CHOICE="${reply}"
      return 0
    fi
    ui_error "Enter a number between 0 and ${#items[@]}."
  done
}

# Runs a command, showing it first, and reports the outcome without letting a
# non-zero exit kill the menu. Menus that die on a failed step force the
# operator to start over, which is how half-finished sequences happen.
ui_run() {
  ui_show_command "$@"
  ui_blank
  local rc=0
  "$@" || rc=$?
  ui_blank
  if (( rc == 0 )); then
    ui_ok "Finished cleanly."
  else
    ui_error "Exited with status ${rc}. Nothing further was run."
  fi
  return "${rc}"
}

ui_pause() {
  local _ignored
  ui_blank
  ui_read "${UI_DIM}Press Enter to continue${UI_RESET} " _ignored ""
}
