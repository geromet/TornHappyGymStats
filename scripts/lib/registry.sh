#!/usr/bin/env bash
# registry.sh — the catalogue of operator tasks scripts/menu.sh can run.
# shellcheck shell=bash
# This file is intended to be sourced.
#
# WHY THIS EXISTS AS DATA
#
# The previous menu.sh hardcoded four tasks and went stale the moment the fifth
# script was written; by the time it was replaced it covered 4 of 27. So the
# catalogue lives here as data, `menu.sh --audit` reports any script in
# scripts/ that no entry drives, and adding a script without adding an entry is
# a visible omission rather than an invisible one.
#
# THIS FILE CONTAINS NO LOGIC THAT TALKS TO THE SERVER.
# Every entry points at an existing script. Nothing here reimplements what a
# script does — the scripts are load-bearing and several of them encode bugs
# that cost a night to find (see docs/OPERATIONS-PITFALLS.md). The menu supplies
# the arguments and the environment gates so the operator does not have to
# remember them; it does not replace the scripts' own gates, which still apply
# when a script is run directly.

[[ -n "${_HGS_REGISTRY_LOADED:-}" ]] && return 0
readonly _HGS_REGISTRY_LOADED=1

# Each entry is one record in REG_ENTRIES, fields separated by RS (0x1f) so that
# any field may safely contain spaces, quotes and pipes.
#
#   1 id          stable key, used by --run <id> for non-interactive use
#   2 category    grouping shown in the main menu
#   3 label       one-line menu text
#   4 script      path relative to scripts/
#   5 preview     args for the SAFE invocation (dry run / status). "-" = none.
#                 "NONE" = this task has no safe preview.
#   6 apply       args for the MUTATING invocation. "NONE" = read-only task.
#   7 apply_env   space-separated VAR=VALUE gates the menu sets when applying
#   8 blurb       what it does, in one sentence
#   9 caution     shown before applying; "-" for none
readonly REG_RS=$'\x1f'
REG_ENTRIES=()

reg_add() {
  local id="$1" category="$2" label="$3" script="$4" preview="$5" apply="$6" apply_env="$7" blurb="$8" caution="${9:--}"
  REG_ENTRIES+=("${id}${REG_RS}${category}${REG_RS}${label}${REG_RS}${script}${REG_RS}${preview}${REG_RS}${apply}${REG_RS}${apply_env}${REG_RS}${blurb}${REG_RS}${caution}")
}

reg_field() {
  local record="$1" index="$2"
  local -a parts
  IFS="${REG_RS}" read -r -a parts <<< "${record}"
  printf '%s' "${parts[$((index - 1))]}"
}

reg_find() {
  local wanted="$1" record
  for record in "${REG_ENTRIES[@]}"; do
    if [[ "$(reg_field "${record}" 1)" == "${wanted}" ]]; then
      printf '%s' "${record}"
      return 0
    fi
  done
  return 1
}

reg_categories() {
  local record
  for record in "${REG_ENTRIES[@]}"; do
    reg_field "${record}" 2
    printf '\n'
  done | awk '!seen[$0]++'
}

# ─────────────────────────────────────────────────────────────────────────────
# Look first
# ─────────────────────────────────────────────────────────────────────────────

reg_add status-devhost "Look first" \
  "Dev host status (units, ports, env files)" \
  "recon-devhost.sh" "-" "NONE" "" \
  "Read-only survey of torndev.geromet.com: units, listeners, release roots, env files."

reg_add status-ports "Look first" \
  "What is listening on the server" \
  "recon-fetch.sh" "ports --sudo" "NONE" "" \
  "Runs the ports collector and saves a timestamped report under workspace/tmp/."

reg_add status-security "Look first" \
  "Server security audit" \
  "recon-fetch.sh" "security --sudo" "NONE" "" \
  "Runs the security collector and saves a timestamped report under workspace/tmp/."

reg_add status-env "Look first" \
  "Are the server's env files usable? (secrets never printed)" \
  "check-server-env.sh" "-" "NONE" "" \
  "Checks every runtime env file for truncation, wrong ownership, leftover REPLACE_ME, and misspelled keys. Reports value lengths, never values."

reg_add status-reboot "Look first" \
  "Is the weekly reboot scheduled?" \
  "setup-auto-reboot.sh" "--status" "NONE" "" \
  "Reports the current unattended-reboot schedule. Changes nothing."

reg_add status-maintenance "Look first" \
  "Maintenance state (swap, heap, postfix, firewall)" \
  "post-reboot-maintenance.sh" "--status" "NONE" "" \
  "Reports which maintenance steps are already applied. Changes nothing."

reg_add status-upgrades "Look first" \
  "Container versions vs targets" \
  "upgrade-containers.sh" "-" "NONE" "" \
  "Surveys Keycloak and PostgreSQL versions and prints the upgrade plan. Changes nothing."

# ─────────────────────────────────────────────────────────────────────────────
# Deploy
# ─────────────────────────────────────────────────────────────────────────────

reg_add deploy-prod-all "Deploy" \
  "Production — API + frontend" \
  "deploy.sh" "NONE" "--target all" "" \
  "Publishes and deploys both production hosts, then restarts their units." \
  "The API applies EF migrations at startup. Check docs/OPERATIONS-PITFALLS.md #8 before deploying a migration you have not reviewed."

reg_add deploy-prod-backend "Deploy" \
  "Production — API only" \
  "deploy.sh" "NONE" "--target backend" "" \
  "Publishes and deploys the production API." \
  "The API applies EF migrations at startup."

reg_add deploy-prod-frontend "Deploy" \
  "Production — frontend only" \
  "deploy.sh" "NONE" "--target frontend" "" \
  "Publishes and deploys the production Blazor host."

reg_add deploy-dev-all "Deploy" \
  "Dev host — API + frontend" \
  "deploy-dev.sh" "NONE" "--target all" "" \
  "Deploys both dev units. Refuses to run if the resolved target is production."

reg_add deploy-dev-backend "Deploy" \
  "Dev host — API only" \
  "deploy-dev.sh" "NONE" "--target backend" "" \
  "Deploys the dev API. First contact with the dev database, so migrations run here first."

reg_add deploy-dev-frontend "Deploy" \
  "Dev host — frontend only" \
  "deploy-dev.sh" "NONE" "--target frontend" "" \
  "Deploys the dev Blazor host."

reg_add deploy-adminpanel "Deploy" \
  "AdminPanel" \
  "deploy-adminpanel.sh" "NONE" "-" "" \
  "Publishes and deploys the AdminPanel host."

reg_add unit-blazor-prod "Deploy" \
  "Install changed systemd unit — production Blazor" \
  "apply-blazor-unit.sh" "NONE" "happygymstats-blazor" "" \
  "Stages infra/happygymstats-blazor.service onto the server, installs it, restarts, and reports the key ring and secret warning." \
  "A unit with EnvironmentFile= will NOT start if that file is missing. Create the env file first — see pitfalls #4."

reg_add unit-blazor-dev "Deploy" \
  "Install changed systemd unit — dev Blazor" \
  "apply-blazor-unit.sh" "NONE" "happygymstats-blazor-dev" "" \
  "Same, for the dev host's Blazor unit."

# ─────────────────────────────────────────────────────────────────────────────
# Server maintenance
# ─────────────────────────────────────────────────────────────────────────────

reg_add maint-run "Server maintenance" \
  "Apply maintenance (swap, Keycloak heap, postfix, docker firewall)" \
  "post-reboot-maintenance.sh" "--status" "--steps swap,keycloak-heap,postfix-loopback,docker-firewall --execute --confirm-maintenance" \
  "DEPLOY_RUN_MAINTENANCE=1" \
  "Applies the post-reboot maintenance steps. Preview shows current state without changing anything." \
  "SSH is a cloudflared tunnel on loopback, so the firewall step cannot lock you out. The step prints its own rollback."

reg_add upgrade-containers "Server maintenance" \
  "Upgrade Keycloak and PostgreSQL containers" \
  "upgrade-containers.sh" "-" "--execute --confirm-upgrade" \
  "DEPLOY_UPGRADE_CONTAINERS=1" \
  "Dumps every database, pulls the new images, then hands you the compose change." \
  "Keycloak migrates its schema forward on first start and that is NOT reversible; rolling back means restoring the dump too. Postgres MAJOR upgrades are refused by the script."

reg_add auto-reboot "Server maintenance" \
  "Schedule the weekly unattended reboot" \
  "setup-auto-reboot.sh" "--status" "--mode weekly --day Sun --time 04:00 --execute --confirm-schedule" \
  "DEPLOY_ENABLE_AUTO_REBOOT=1" \
  "Schedules a weekly reboot so installed kernel patches take effect." \
  "War nights: touch /etc/happygymstats/no-reboot to hold the machine up, rm it to release. Honoured in weekly mode."

reg_add remove-teamspeak "Server maintenance" \
  "Remove the TeamSpeak container" \
  "remove-teamspeak.sh" "-" "--execute --confirm-remove" \
  "DEPLOY_REMOVE_TEAMSPEAK=1" \
  "Decommissions TeamSpeak, closing 30033/tcp and 9987/udp — the only two ports that bypass the firewall." \
  "Volumes are backed up and KEPT. Only --delete-volumes removes them, and this menu never passes it."

# ─────────────────────────────────────────────────────────────────────────────
# Dev host bootstrap
# ─────────────────────────────────────────────────────────────────────────────

reg_add devhost-contract "Dev host" \
  "Check the dev-host contract (offline)" \
  "verify/devhost-contract.sh" "-" "NONE" "" \
  "Static check that the dev units cannot point at production roots, ports or env files. No host needed."

reg_add devhost-setup "Dev host" \
  "Bootstrap the dev host (nginx, units, roots, env skeletons)" \
  "setup-devhost-server.sh" "-" "--execute --confirm-remote-setup" \
  "DEPLOY_INSTALL_DEV_HOST=1" \
  "Installs the nginx block, both dev units, the release roots, and seeds both env files." \
  "Existing env files are never overwritten. Both seeded files contain REPLACE_ME values that must be edited before the services will start."

reg_add devhost-smoke "Dev host" \
  "Smoke-test the dev host" \
  "verify/devhost-smoke.sh" "-" "NONE" "" \
  "Checks the dev units are active, the API answers on loopback, and the public route challenges anonymous visitors."

reg_add adminpanel-setup "Dev host" \
  "Bootstrap the AdminPanel nginx route" \
  "setup-adminpanel-server.sh" "-" "--execute --confirm-remote-setup" \
  "DEPLOY_INSTALL_ADMIN_NGINX=1" \
  "Installs the AdminPanel nginx route. Validates with nginx -t before reloading."

# ─────────────────────────────────────────────────────────────────────────────
# Look at it
# ─────────────────────────────────────────────────────────────────────────────

reg_add shots-war "Look at it" \
  "Screenshot the war board (phone / tablet / desktop)" \
  "screenshot-board.sh" "-" "NONE" "" \
  "Boots the app locally with dev auth and the seeded war, shoots every viewport into workspace/tmp/screenshots, then stops both hosts. Nothing touches the server."

reg_add shots-setup "Look at it" \
  "Install the screenshot tooling (Playwright + its own Chromium)" \
  "screenshot-board.sh" "--check" "--setup" "" \
  "Creates .venv and downloads Chromium into ~/.cache/ms-playwright. No sudo; no browser you use yourself is involved." \
  "Downloads about 115 MB on first run."

# ─────────────────────────────────────────────────────────────────────────────
# Verify
# ─────────────────────────────────────────────────────────────────────────────

reg_add verify-all "Verify" \
  "Build, test and all contract checks" \
  "verify/build-and-test.sh" "-" "NONE" "" \
  "The full local gate: build, unit tests, and every wired contract verifier."

reg_add verify-heredoc "Verify" \
  "Remote-heredoc lint (offline)" \
  "verify/remote-heredoc-lint.sh" "-" "NONE" "" \
  "Catches unescaped expansions in remote heredocs — the bug class behind pitfall #2."

reg_add verify-chain "Verify" \
  "M008 chain-command contract" \
  "verify/w06-chain-contract.sh" "-" "NONE" "" \
  "Pinned chain acceptance tests, board literals, and the Core boundary guardrail."

reg_add verify-graph "Verify" \
  "Verifier graph (manifest completeness, offline)" \
  "verify/verifier-graph.sh" "-" "NONE" "" \
  "Fails if a verifier script has no manifest row, a row points at a deleted script, or an exclusion has no stated reason."

reg_add verify-hermetic "Verify" \
  "Hermetic test suite (developer config stripped)" \
  "verify/hermetic-tests.sh" "-" "NONE" "" \
  "Runs the non-Postgres tests with ConnectionStrings__*, HAPPYGYMSTATS_* and host appsettings removed, so a workstation and a clean runner agree."

reg_add verify-honest-signal "Verify" \
  "U001 honest-signal contract (offline)" \
  "verify/u001-honest-signal.sh" "-" "NONE" "" \
  "Checks every war-board figure declares whether it is measured, projected or inferred."

reg_add verify-vault "Verify" \
  "M009 key-vault contract" \
  "verify/w07-key-vault-contract.sh" "-" "NONE" "" \
  "Key-vault acceptance tests plus the source rules no runtime test can prove."

reg_add verify-prod-smoke "Verify" \
  "Production smoke test" \
  "verify/production-smoke.sh" "-" "NONE" "" \
  "Checks the production units, routes and health endpoints."

# ─────────────────────────────────────────────────────────────────────────────
# Scripts deliberately NOT in the menu
#
# Listed by `menu.sh --audit` as known exclusions so the audit stays quiet and
# the reason is written down rather than rediscovered.
# ─────────────────────────────────────────────────────────────────────────────
# shellcheck disable=SC2034  # consumed by menu.sh --audit after sourcing.
REG_EXCLUDED=(
  "menu.sh:this menu"
  "deploy-config.sh:sourced library, not a task"
  "deploy-backend.sh:driven through deploy.sh / deploy-dev.sh, which set the correct roots and units"
  "deploy-frontend.sh:driven through deploy.sh / deploy-dev.sh"
  "publish.sh:build step the deploy scripts already run"
  "recon-server.sh:collector body, run on the server by recon-fetch.sh"
  "recon-ports.sh:collector body, run on the server by recon-fetch.sh"
  "audit-server-security.sh:collector body, run on the server by recon-fetch.sh"
  "recon-devhost-fetch.sh:superseded alias for recon-fetch.sh, kept for old muscle memory"
  "install-docker.sh:one-time server bootstrap, run once and done"
  "server-create-containers-user.sh:one-time server bootstrap, run once and done"
  "deploy-containers.sh:container stack deploy, superseded by upgrade-containers.sh for the live stack"
  "github-auth.sh:local developer setup, not a server operation"
  "verifier-graph-regression.sh:negative control for the verifier graph, run by the canonical gate"
  "verify-common.sh:sourced library of fail-closed primitives, not a runnable task"
  "verify-s01-taxonomy.sh:one-off milestone check, superseded by the wNN contract verifiers"
)
