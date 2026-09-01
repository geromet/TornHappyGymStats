# S06: Normalize deployment scripts — UAT

**Milestone:** M003
**Written:** 2026-05-07T19:39:59.930Z

# S06 UAT — Normalize deployment scripts

## UAT Type

Local operational-contract UAT for deployment scripts. This UAT validates shell syntax, non-mutating help/contract behavior, machine-readable precondition and safety markers, shared SSH/config usage, and canonical smoke-hook wiring. It does not require production credentials and does not mutate the remote server.

## Preconditions

- Run from the repository root (`/Project`).
- Do not provide production secrets in command output.
- Do not set `DEPLOY_RUN_SMOKE=1` unless intentionally testing against production; this UAT covers the default safe/local contract.
- The scripts under `scripts/` and docs under `docs/DEPLOYMENT.md` are present.

## Test Cases

### 1. Container deploy uses shared config and no duplicated legacy SSH literals

Steps:
1. Run `bash -n scripts/deploy-containers.sh`.
2. Run `bash scripts/deploy-containers.sh --help`.
3. Run `! rg -n "id_token2_bio3_hetzner|cloudflared access ssh|anon@ssh\.geromet\.com" scripts/deploy-containers.sh`.

Expected outcomes:
- Syntax check exits 0.
- Help exits 0 and describes required variables/preconditions without printing key contents or secret values.
- Legacy SSH host/key/proxy literals are not present in `deploy-containers.sh`.

### 2. App deploys fail before publish/restart when setup is missing

Steps:
1. Run `bash -n scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh`.
2. Run `rg -n "precheck|precondition|required|setup-adminpanel-server|is-active|systemctl status" scripts/deploy-*.sh scripts/deploy-config.sh`.

Expected outcomes:
- Syntax check exits 0.
- Scripts contain shared precondition markers/checks.
- AdminPanel deploy exposes setup guidance referencing `setup-adminpanel-server` before restart-time failure.

### 3. Deploy orchestration points to the canonical production smoke gate

Steps:
1. Run `bash -n scripts/deploy.sh scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh`.
2. Run `rg -n "production-smoke|DEPLOY_RUN_SMOKE|smoke" scripts/deploy*.sh`.
3. Run `bash scripts/deploy.sh --help | rg -n "DEPLOY_RUN_SMOKE|DEPLOY_SMOKE_MODE|production-smoke"`.

Expected outcomes:
- Syntax check exits 0.
- Deploy scripts reference the shared smoke hook or production smoke next step.
- Orchestrator help documents optional smoke execution and smoke mode controls.

### 4. Manual bootstrap and read-only diagnostics are machine-classified

Steps:
1. Run `bash -n scripts/setup-adminpanel-server.sh scripts/verify/production-smoke.sh`.
2. Run `rg -n "SCRIPT_CATEGORY|SCRIPT_MUTATES_SERVER_STATE|SCRIPT_AUTOMATION_SAFE_DEFAULT" scripts/setup-adminpanel-server.sh scripts/verify/production-smoke.sh docs/DEPLOYMENT.md`.
3. Run `! rg -n "Paste full output back|Copy .* manually|ask Claude" scripts/setup-adminpanel-server.sh scripts/verify/production-smoke.sh docs/DEPLOYMENT.md`.

Expected outcomes:
- Syntax check exits 0.
- Manual bootstrap, diagnostic, and documentation surfaces expose category/safety markers.
- Operational scripts/docs no longer depend on paste-back human-loop instructions.

### 5. Deterministic S06 contract verifier catches drift locally

Steps:
1. Run `bash scripts/verify/s06-deploy-script-contract.sh`.

Expected outcomes:
- Command exits 0.
- Output includes PASS lines for syntax, shared SSH literal policy, release activation tokens, AdminPanel setup hint, and smoke-hook wiring.
- Output ends with `RESULT failures=0`.

## Edge Cases

- If `infra/docker-compose.yml` is absent, `scripts/deploy-containers.sh` should fail in precheck or document the required override rather than proceeding to remote mutation.
- If the AdminPanel project path is absent, `scripts/deploy-adminpanel.sh` should report a missing project precondition rather than publishing an empty release.
- If AdminPanel service setup is absent remotely, deploy should point to `scripts/setup-adminpanel-server.sh --help` instead of surfacing only a generic `systemctl restart` failure.
- If `DEPLOY_RUN_SMOKE=1` is enabled and smoke fails, `scripts/deploy.sh` should fail the overall deploy rather than claiming stack verification succeeded.

## Not Proven By This UAT

- A live remote backend/frontend/AdminPanel/container deployment.
- Real SSH, sudo, systemd, nginx, Docker, Postgres, or Keycloak behavior on the production host.
- Performance under load or long-running monitoring/alerting.
- Runtime/package reproducibility; that remains for S09.
- Public documentation completeness for operators; that remains for S08.
