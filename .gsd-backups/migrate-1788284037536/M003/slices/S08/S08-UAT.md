# S08: Refactor docs and API examples into current-state contract — UAT

**Milestone:** M003
**Written:** 2026-05-07T20:09:33.171Z

# S08: Refactor docs and API examples into current-state contract — UAT

**Milestone:** M003
**Written:** 2026-05-07

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: S08 shipped documentation and runnable-ish API examples rather than runtime code. The acceptance contract is that repo artifacts describe the current Blazor + API + Postgres + Keycloak + AdminPanel shape, expose the right verification commands, and reject known stale route/runtime claims.

## Preconditions

- The repository checkout contains the completed S08 artifacts: `README.md`, `docs/OVERVIEW.md`, `docs/SETUP.md`, `docs/DEPLOYMENT.md`, `src/HappyGymStats.Api/HappyGymStats.Api.http`, and `scripts/verify/s08-docs-contract.sh`.
- `rg`, `grep`, and `bash` are available in the local shell.
- No production credentials are required; docs may name env vars but must not contain secret values.

## Smoke Test

Run:

```bash
bash scripts/verify/s08-docs-contract.sh
```

Expected: command exits 0 and prints `S08 docs contract drift checks passed.`

## Test Cases

### 1. README describes the current operational shape

1. Open `README.md`.
2. Confirm it names Blazor, Postgres/PostgreSQL, Keycloak, AdminPanel, production smoke verification, and the M003 audit context token `2026-05-06-181943` or its roadmap/audit reference.
3. Confirm `web/` static frontend language is marked legacy/historical rather than primary.
4. **Expected:** README can orient a new agent to the current production architecture without claiming SQLite/static dashboard as the active path.

### 2. Architecture overview matches current data flow and boundaries

1. Open `docs/OVERVIEW.md`.
2. Confirm it distinguishes repo-owned projects from operational peers.
3. Confirm it describes import/reconstruction/surfaces flow, `/api/v1/torn/...` surfaces endpoints, Postgres/Keycloak/AdminPanel, and loopback ports `127.0.0.1:5047`, `127.0.0.1:5182`, and `127.0.0.1:5048`.
4. **Expected:** Overview gives enough current architecture context to plan work without rediscovering the refactor.

### 3. Setup and deployment docs expose the production contract

1. Open `docs/SETUP.md` and `docs/DEPLOYMENT.md`.
2. Confirm they include required config names such as `HAPPYGYMSTATS_CONNECTION_STRING`, `ProvisionalToken__SigningKey`, and `HAPPYGYMSTATS_SURFACES_CACHE_DIR`.
3. Confirm they document `--no-launch-profile`, `setup-adminpanel-server`, `production-smoke`, and service names `happygymstats-api`, `happygymstats-blazor`, and `happygymstats-adminpanel`.
4. **Expected:** Docs explain one-time setup versus steady-state deploy and point operators to the canonical smoke script instead of ad hoc checks.

### 4. API examples use live route contract

1. Open `src/HappyGymStats.Api/HappyGymStats.Api.http`.
2. Confirm examples include `GET /api/v1/torn/health`, `GET /api/v1/torn/surfaces/latest`, and `/api/v1/torn/import-jobs` routes.
3. Confirm no examples use stale `localhost:5047/v1`, `GET ... /v1/`, or `POST ... /v1/` route forms.
4. **Expected:** `.http` examples can be used as runnable documentation for the current Minimal API routes.

## Edge Cases

### Stale-route regression is introduced

1. Temporarily add a stale route claim such as `GET http://localhost:5047/v1/health` to `src/HappyGymStats.Api/HappyGymStats.Api.http`.
2. Run `bash scripts/verify/s08-docs-contract.sh`.
3. **Expected:** verifier exits non-zero and reports the stale route/runtime claim. Revert the temporary change afterward.

### Dash-prefixed marker remains checkable

1. Confirm `docs/SETUP.md` or `docs/DEPLOYMENT.md` includes `--no-launch-profile`.
2. Run `bash scripts/verify/s08-docs-contract.sh`.
3. **Expected:** verifier handles the dash-prefixed token correctly and still exits 0, proving the grep guard uses option-safe matching.

## Failure Signals

- `bash scripts/verify/s08-docs-contract.sh` exits non-zero.
- Docs omit current service names, env var names, production smoke command, or `/api/v1/torn/...` route markers.
- Docs present SQLite storage or the static dashboard as the primary current architecture rather than compatibility/legacy context.
- `.http` examples call stale `/v1/*` routes.
- Docs include secret values instead of only env var names and secret-handling procedure.

## Not Proven By This UAT

- It does not prove live production endpoints are healthy; S05 `production-smoke` owns live stack verification.
- It does not prove Docker/Postgres Testcontainers can run in this environment; S07/S09 own provider/runtime reproducibility checks.
- It does not prove human prose clarity beyond mechanical current-state markers; a human operator should still read for usability.
- It does not prove missing source projects for Blazor/AdminPanel exist locally; docs intentionally describe them as operational peers in this checkout.

## Notes for Tester

The audit report named in the original plan was not present under `docs/`; S08 documents the M003 roadmap/audit context instead while preserving the audit timestamp token for traceability. When testing stale-regression behavior, use a disposable edit and revert it before committing.
