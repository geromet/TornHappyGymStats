# S03: Installable AdminPanel server setup

**Goal:** Create a safe, idempotent bootstrap path for AdminPanel server prerequisites that are currently only local artifacts. The setup must respect the likely current server constraint: only a narrow NOPASSWD rsync permission may already be installed.
**Demo:** A one-time setup flow installs sudoers/service prerequisites, enables AdminPanel, and verifies `/admin/health`.

## Must-Haves

- Setup flow installs or updates the AdminPanel systemd service from `infra/happygymstats-adminpanel.service` to the correct systemd path.
- Setup flow runs `systemctl daemon-reload` and enables/starts `happygymstats-adminpanel` when appropriate.
- Setup flow installs the sudoers deployment artifact safely via temp file plus syntax validation before activation.
- Sudoers command scope is intentionally split or documented for one-time setup vs steady-state deployment.
- Re-running setup is safe: it does not duplicate config, break existing services, or widen privileges unexpectedly.
- Loopback `/admin/health` on `127.0.0.1:5048` is verified after setup/start.
- Failure output names missing privilege, failed sudoers validation, failed systemd operation, or failed health check distinctly.

## Proof Level

- This slice proves: Operational setup proof. Full proof requires executing against a target server with explicit user confirmation before outward mutation; local proof must include syntax/static verification and idempotency checks.

## Integration Closure

Upstream surfaces consumed: `infra/happygymstats-adminpanel.service`, `infra/sudoers-happygymstats`, `scripts/deploy-adminpanel.sh`, `scripts/deploy-config.sh`, and existing API/Blazor deploy patterns.
New wiring introduced: one-time setup script for sudoers/systemd AdminPanel prerequisites and loopback health verification.
What remains before milestone end-to-end: S04 must expose AdminPanel through nginx; S06 must normalize deploy scripts around the setup boundary.

## Verification

- Runtime signals: setup phase output for sudoers validation, service file install, daemon reload, enable/start, and `/admin/health` loopback check.
- Inspection surfaces: setup script output, `systemctl status happygymstats-adminpanel`, `/admin/health`, installed sudoers file.
- Failure visibility: missing privilege, failed rsync/bootstrap copy, failed sudoers syntax, failed daemon reload, failed enable/start, failed health check.
- Redaction constraints: no connection string, Keycloak secret, or auth token output.

## Tasks

- [ ] **T01: Define AdminPanel sudoers privilege boundary** `est:1h`
  Why: The local sudoers file mixes steady-state deploy commands with missing one-time setup needs; the bootstrap privilege model must be explicit before scripting it.
  - Files: `infra/sudoers-happygymstats`, `scripts/deploy-adminpanel.sh`, `docs/DEPLOYMENT.md`
  - Verify: rg -n "happygymstats-adminpanel|daemon-reload|enable|start|visudo|sudoers|NOPASSWD" infra/sudoers-happygymstats docs/DEPLOYMENT.md && ! rg -n "NOPASSWD: ALL|/bin/bash|/usr/bin/bash|sh -c" infra/sudoers-happygymstats

- [ ] **T02: Create AdminPanel systemd setup script** `est:2h`
  Why: AdminPanel deploy assumes the service exists, but no script installs the service file or enables it.
  - Files: `scripts/setup-adminpanel-server.sh`, `scripts/deploy-config.sh`, `infra/happygymstats-adminpanel.service`
  - Verify: bash -n scripts/setup-adminpanel-server.sh && bash scripts/setup-adminpanel-server.sh --help && rg -n "daemon-reload|enable|happygymstats-adminpanel.service|/etc/systemd/system|systemctl" scripts/setup-adminpanel-server.sh

- [ ] **T03: Add safe sudoers install validation** `est:1.5h`
  Why: Installing sudoers unsafely can lock out deployment or widen privileges unexpectedly; the setup path needs a validated atomic install pattern.
  - Files: `scripts/setup-adminpanel-server.sh`, `infra/sudoers-happygymstats`
  - Verify: bash -n scripts/setup-adminpanel-server.sh && rg -n "visudo|sudoers.d|chmod 440|install|happygymstats" scripts/setup-adminpanel-server.sh infra/sudoers-happygymstats

- [ ] **T04: Verify AdminPanel loopback health after setup** `est:1h`
  Why: Setup is only useful if it proves the service is alive on the loopback port that S04/nginx will consume.
  - Files: `scripts/setup-adminpanel-server.sh`, `scripts/verify/s03-adminpanel-setup.sh`, `docs/DEPLOYMENT.md`
  - Verify: bash scripts/verify/s03-adminpanel-setup.sh

## Files Likely Touched

- infra/sudoers-happygymstats
- scripts/deploy-adminpanel.sh
- docs/DEPLOYMENT.md
- scripts/setup-adminpanel-server.sh
- scripts/deploy-config.sh
- infra/happygymstats-adminpanel.service
- scripts/verify/s03-adminpanel-setup.sh
