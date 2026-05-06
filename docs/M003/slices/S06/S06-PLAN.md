# S06: Normalize deployment scripts

**Goal:** Reduce deployment drift by consolidating script configuration and making preconditions explicit. Keep one-time setup separate from steady-state deploy while reusing the smoke checks established earlier.
**Demo:** Backend, frontend, admin, and container deploy scripts share config, avoid hardcoded SSH duplication, and report machine-checkable preconditions.

## Must-Haves

- Backend, frontend, AdminPanel, and container deploy flows use shared deployment configuration where practical.
- Hardcoded SSH host/key/proxy duplication is removed or minimized.
- Scripts check required local files, remote directories, service names, and required privileges before publishing/restarting.
- Scripts distinguish one-time setup responsibilities from normal deploy responsibilities.
- Scripts call or clearly point to the production smoke script after deploy.
- Human-paste instructions in operational scripts are replaced with machine-readable output or documented manual-only scripts.
- Existing release/symlink behavior remains intact for API, Blazor, and AdminPanel.

## Proof Level

- This slice proves: Script behavior proof through shell verification and safe dry-run/read-only checks. Remote mutation requires explicit user confirmation during execution.

## Integration Closure

Upstream surfaces consumed: S01 backend health gates, S03 setup boundary, S05 production smoke script, existing deploy scripts and container scripts.
New wiring introduced: shared deploy config/patterns across backend, Blazor, AdminPanel, and containers.
What remains before milestone end-to-end: docs in S08 must describe the normalized deployment flow.

## Verification

- Runtime signals: deploy precondition output, shared SSH/config resolution output, post-deploy smoke invocation/recommendation.
- Inspection surfaces: deploy script logs, `deploy-config.sh`, production smoke output.
- Failure visibility: missing local file, missing remote privilege, missing service, missing env, failed publish, failed activation, failed smoke.
- Redaction constraints: no SSH key contents, secret env values, connection strings, tokens, or API keys printed.

## Tasks

- [ ] **T01: Move container deploy onto shared config** `est:1.5h`
  Why: Container deploy currently hardcodes SSH details while app deploys use shared config. This creates drift and makes future server changes error-prone.
  - Files: `scripts/deploy-containers.sh`, `scripts/deploy-config.sh`
  - Verify: bash -n scripts/deploy-containers.sh && bash scripts/deploy-containers.sh --help && ! rg -n "id_token2_bio3_hetzner|cloudflared access ssh|anon@ssh\.geromet\.com" scripts/deploy-containers.sh

- [ ] **T02: Add shared deploy preconditions** `est:2h`
  Why: Each deploy script should fail before publishing when required files, commands, privileges, or setup state are missing.
  - Files: `scripts/deploy-config.sh`, `scripts/deploy-backend.sh`, `scripts/deploy-frontend.sh`, `scripts/deploy-adminpanel.sh`, `scripts/setup-adminpanel-server.sh`
  - Verify: bash -n scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "precheck|precondition|required|setup-adminpanel-server|is-active|systemctl status" scripts/deploy-*.sh scripts/deploy-config.sh

- [ ] **T03: Wire deploy flow to production smoke** `est:1h`
  Why: Deploy scripts should not duplicate smoke logic, but they should make the canonical post-deploy verification obvious and optionally automatic.
  - Files: `scripts/deploy.sh`, `scripts/deploy-config.sh`, `scripts/deploy-backend.sh`, `scripts/deploy-frontend.sh`, `scripts/deploy-adminpanel.sh`, `scripts/verify/production-smoke.sh`
  - Verify: bash -n scripts/deploy.sh scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "production-smoke|DEPLOY_RUN_SMOKE|smoke" scripts/deploy*.sh

- [ ] **T04: Categorize manual and diagnostic ops scripts** `est:1h`
  Why: Some existing scripts prompt humans or ask for pasted output; those are risky in agent-driven deployment and should be clearly manual-only or machine-readable.
  - Files: `scripts/recon-server.sh`, `scripts/server-create-containers-user.sh`, `docs/DEPLOYMENT.md`
  - Verify: bash -n scripts/recon-server.sh scripts/server-create-containers-user.sh && ! rg -n "Paste full output back|Copy .* manually|ask Claude" scripts/recon-server.sh scripts/server-create-containers-user.sh docs/DEPLOYMENT.md

- [ ] **T05: Add deployment script contract verifier** `est:45m`
  Why: S06 changes several scripts and needs a deterministic local verifier to catch drift without touching production.
  - Files: `scripts/verify/s06-deploy-script-contract.sh`, `scripts/deploy-config.sh`, `scripts/deploy-backend.sh`, `scripts/deploy-frontend.sh`, `scripts/deploy-adminpanel.sh`, `scripts/deploy-containers.sh`
  - Verify: bash scripts/verify/s06-deploy-script-contract.sh

## Files Likely Touched

- scripts/deploy-containers.sh
- scripts/deploy-config.sh
- scripts/deploy-backend.sh
- scripts/deploy-frontend.sh
- scripts/deploy-adminpanel.sh
- scripts/setup-adminpanel-server.sh
- scripts/deploy.sh
- scripts/verify/production-smoke.sh
- scripts/recon-server.sh
- scripts/server-create-containers-user.sh
- docs/DEPLOYMENT.md
- scripts/verify/s06-deploy-script-contract.sh
