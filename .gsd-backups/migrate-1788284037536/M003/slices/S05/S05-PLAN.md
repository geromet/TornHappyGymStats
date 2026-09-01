# S05: S05

**Goal:** Capture the production failure boundary in a repeatable smoke script so future deploys catch 502-class failures before users do. The script must be safe, read-only, and concise enough for agents/operators to trust.
**Demo:** One command verifies systemd units, nginx routes, API health, surfaces endpoint, Blazor home, AdminPanel health, and container health.

## Must-Haves

- Smoke script checks API, Blazor, and AdminPanel systemd unit status.
- Smoke script validates nginx configuration and route responses without mutating state.
- Smoke script checks API loopback health, API external health, surfaces latest endpoint, Blazor home reachability, AdminPanel loopback health, and AdminPanel external health.
- Smoke script checks Postgres and Keycloak container health or clearly reports when container access is unavailable.
- Output is structured by phase with pass/fail and actionable failure messages.
- Script exits non-zero on any required check failure and documents optional/skippable checks.
- Script avoids printing secrets and does not require Torn API key for baseline health checks.

## Proof Level

- This slice proves: Operational full-stack proof. Local/simulated mode is acceptable during development; full closure requires execution against intended production target with explicit confirmation for any remote access if commands are not purely local read-only.

## Integration Closure

Upstream surfaces consumed: S01 API checks, S02 Blazor boundary, S04 AdminPanel route checks, service names from deploy config, nginx configs, Postgres/Keycloak container layout.
New wiring introduced: canonical read-only production smoke command.
What remains before milestone end-to-end: S06 should reuse/reference this script from deploy flows; S08 should document it.

## Verification

- Runtime signals: one phase-organized pass/fail report for systemd, nginx, API, surfaces, Blazor, AdminPanel, Postgres, and Keycloak.
- Inspection surfaces: smoke script output, exit code, optional JSON/log artifact if added.
- Failure visibility: missing service, inactive service, bad nginx config, bad gateway, no surfaces cache, auth boundary regression, container unhealthy.
- Redaction constraints: no secrets; do not require Torn API key; do not print env file contents.

## Tasks

- [x] **T01: Created scripts/verify/production-smoke.sh as a read-only production smoke framework with shared phase/result helpers, local/remote execution mode, and deterministic required-vs-optional exit semantics.** `est:1.5h`
  Why: The smoke script needs shared primitives so each check reports consistently and safely.
  - Files: `scripts/verify/production-smoke.sh`, `scripts/deploy-config.sh`
  - Verify: bash -n scripts/verify/production-smoke.sh && bash scripts/verify/production-smoke.sh --help && rg -n "PASS|FAIL|WARN|required|optional|secret|TOKEN|KEY" scripts/verify/production-smoke.sh

- [x] **T02: Added a services phase to production smoke that checks systemd unit state, nginx syntax, and required listener ports with explicit failure classification.** `est:1h`
  Why: The reported 502 is a systemd/nginx/API boundary failure; smoke must check these before UI assertions.
  - Files: `scripts/verify/production-smoke.sh`, `scripts/deploy-config.sh`
  - Verify: bash -n scripts/verify/production-smoke.sh && rg -n "happygymstats-api|happygymstats-blazor|happygymstats-adminpanel|nginx -t|5047|5182|5048|is-active|ss " scripts/verify/production-smoke.sh

- [x] **T03: Added production smoke HTTP-route phase covering API health/surfaces, Blazor home, and AdminPanel health/auth-boundary checks with safe failure excerpts.** `est:1.5h`
  Why: The core user path requires API health, surfaces latest, and Blazor home all to work together.
  - Files: `scripts/verify/production-smoke.sh`
  - Verify: bash -n scripts/verify/production-smoke.sh && rg -n "api/v1/torn/health|api/v1/torn/surfaces/latest|admin/health|admin/api/v1/import-runs|Bad Gateway|502|Blazor|curl" scripts/verify/production-smoke.sh

- [x] **T04: Added an optional container-health phase to production smoke that reports Postgres/Keycloak running-health vs docker-access-unavailable states without exposing secrets.** `est:1h`
  Why: API startup and auth depend on Postgres and Keycloak containers, so smoke must report container state without requiring credentials.
  - Files: `scripts/verify/production-smoke.sh`, `infra/docker-compose.yml`
  - Verify: bash -n scripts/verify/production-smoke.sh && rg -n "postgres|keycloak|docker compose|docker ps|container|healthy|unhealthy" scripts/verify/production-smoke.sh

- [x] **T05: Added a smoke-contract verifier and deployment runbook section that enforce and document the production-smoke failure boundary.** `est:45m`
  Why: The smoke script is the milestone’s main operational proof and must be documented and locally verifiable before being wired into deploy scripts.
  - Files: `scripts/verify/s05-production-smoke-contract.sh`, `scripts/verify/production-smoke.sh`, `docs/DEPLOYMENT.md`
  - Verify: bash scripts/verify/s05-production-smoke-contract.sh

- [ ] **T06: Harden production smoke security and required surfaces semantics** `est:1h`
  Remediate slice-closer blockers in the production smoke script. Change surfaces/latest 404/no-cache handling from required PASS to required FAIL while keeping actionable failure text. Eliminate or mitigate env-controlled shell command injection in local and remote modes by validating SMOKE_* inputs and safely quoting dynamic command arguments, or by replacing shell-string execution with fixed script bodies/argv-safe calls where practical. Add the documented framework phase to s05-production-smoke-contract.sh. Re-run the contract verifier and static checks proving the prior blocker signatures are gone. Because the script can execute over SSH, preserve read-only behavior and do not print env contents or secrets.
  - Files: `scripts/verify/production-smoke.sh`, `scripts/verify/s05-production-smoke-contract.sh`
  - Verify: bash -n scripts/verify/production-smoke.sh && bash scripts/verify/s05-production-smoke-contract.sh && gsd_exec/static equivalent proving: surfaces 404 no longer passes, documented framework phase is covered, and env-derived URL/service/container hint values are either validated and shell-quoted or not interpolated into bash -lc strings unsafely. Then repeat reviewer/security assessment before slice completion.

## Files Likely Touched

- scripts/verify/production-smoke.sh
- scripts/deploy-config.sh
- infra/docker-compose.yml
- scripts/verify/s05-production-smoke-contract.sh
- docs/DEPLOYMENT.md
