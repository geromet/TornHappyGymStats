# M003: Production deploy recovery and refactor hardening

**Vision:** Recover the production deployment after the refactor by making the API, Blazor frontend, AdminPanel, nginx, systemd, sudoers, and smoke verification behave as one observable system. The immediate user-visible problem is the Blazor surfaces page returning 502 from the backend; the broader goal is to make this class of deployment/configuration failure fail fast during deployment instead of surfacing as an opaque UI error.

## Success Criteria

- The deployed API has an explicit production runtime contract for connection string, token signing key, surfaces cache directory, and listening URL, and deploy verification proves both loopback and nginx API health paths work.
- The Blazor frontend loads surfaces data through the chosen production API boundary without returning 502, and frontend/backend diagnostics distinguish API-down, nginx-bad-gateway, missing-cache, and import-failure cases.
- AdminPanel has an idempotent server setup path that installs/validates sudoers, installs/enables its systemd service, and proves `/admin/health` works on loopback.
- AdminPanel has an intentional nginx exposure path, with anonymous health reachable and admin APIs remaining auth-gated.
- One production smoke command verifies the assembled stack: systemd units, nginx config/routes, API health, surfaces endpoint, Blazor home, AdminPanel health, and Postgres/Keycloak container state.
- Deployment scripts share configuration, avoid hardcoded SSH drift, run machine-checkable preconditions, and fail before publishing or restarting when required server prerequisites are absent.
- Docs and API examples describe the current Blazor + API + Postgres + Keycloak deployment shape rather than the stale SQLite/static-frontend shape.

## Slices

- [x] **S01: S01** `risk:high` `depends:[]`
  > After this: API deploy fails fast if Postgres/env/service startup is broken, and `/api/v1/torn/health` can be verified through both loopback and nginx.

- [x] **S02: S02** `risk:high` `depends:[]`
  > After this: Blazor can load surfaces without 502, and UI errors distinguish API down, nginx bad gateway, 404 no-cache, and import failure.

- [x] **S03: S03** `risk:high` `depends:[]`
  > After this: A one-time setup flow installs sudoers/service prerequisites, enables AdminPanel, and verifies `/admin/health`.

- [x] **S04: S04** `risk:medium` `depends:[]`
  > After this: AdminPanel is reachable through its intended nginx host/path, health is public as intended, and protected endpoints remain auth-gated.

- [x] **S05: S05** `risk:medium` `depends:[]`
  > After this: One command verifies systemd units, nginx routes, API health, surfaces endpoint, Blazor home, AdminPanel health, and container health.

- [x] **S06: S06** `risk:medium` `depends:[]`
  > After this: Backend, frontend, admin, and container deploy scripts share config, avoid hardcoded SSH duplication, and report machine-checkable preconditions.

- [x] **S07: S07** `risk:medium` `depends:[]`
  > After this: Tests can run migrations against a real Postgres provider and hit health/surfaces paths, catching startup failures SQLite tests miss.

- [x] **S08: S08** `risk:low` `depends:[]`
  > After this: README, docs, and `.http` examples match the actual Blazor + API + Postgres + Keycloak + deployment shape.

- [x] **S09: S09** `risk:low` `depends:[]`
  > After this: The repo documents and verifies the expected .NET runtime/SDK and package restore behavior for deploys.

- [x] **S10: S10** `risk:high` `depends:[]`
  > After this: After this: S03/S04/S05 have reliable replacement closure evidence from task artifacts or are explicitly re-executed, and M003 requirement coverage states exactly which requirements are in scope without claiming unsupported R001/R002 evidence.

- [x] **S11: Produce live full-stack deployment evidence** `risk:high` `depends:[S10]`
  > After this: After this: validation has a current production or production-like smoke report covering API loopback/nginx health, Blazor surfaces no-502 behavior, AdminPanel loopback/nginx health and auth boundary, Postgres/Keycloak state, and Postgres-provider migration/endpoint coverage.

## Boundary Map

### S01 → S02

Produces:
- Verified production API runtime contract: required env vars, service URL, Postgres readiness, surfaces cache directory, and `/api/v1/torn/health` loopback/nginx checks.
- Deploy-time health gate pattern for API restart.
- Evidence showing whether API 502 is due to service startup, database, nginx, or route configuration.

Consumes:
- Audit report at `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md`.

### S01 → S05

Produces:
- Stable API health commands and expected response semantics usable by the full-stack smoke script.
- Known production env contract for API/service checks.

Consumes:
- None beyond existing deploy scripts and service files.

### S01 → S07

Produces:
- Production-provider startup/migration assumptions that the Postgres integration test must exercise.

Consumes:
- Existing API/Data project structure and migrations.

### S01 + S02 → S05

Produces:
- Proven Blazor-to-API boundary and endpoint diagnostics that the production smoke script must verify.

Consumes:
- S01 API health contract.

### S03 → S04

Produces:
- Installed AdminPanel systemd unit contract: service name, port `127.0.0.1:5048`, `/admin/health` loopback check, and sudoers setup boundary.

Consumes:
- Existing `infra/happygymstats-adminpanel.service`, `infra/sudoers-happygymstats`, and deploy config.

### S03 → S06

Produces:
- Idempotent privileged setup conventions that normalized deployment scripts must respect instead of duplicating ad hoc server commands.

Consumes:
- Existing release/symlink deploy scripts.

### S04 → S05

Produces:
- AdminPanel external route, nginx validation command, and protected/public endpoint expectations for the full-stack smoke script.

Consumes:
- S03 AdminPanel service setup.

### S05 → S06

Produces:
- Canonical production smoke command and failure taxonomy; deploy scripts should call or reference this instead of implementing divergent checks.

Consumes:
- S01/S02/S04 runtime contracts.

### S01 + S05 → S08

Produces:
- Verified current runtime/deployment behavior that docs must describe.

Consumes:
- Existing README/docs and API `.http` examples.

### S05 → S09

Produces:
- Observed runtime/SDK/package assumptions from the deployed stack.

Consumes:
- Build/deploy scripts and project/package files.
