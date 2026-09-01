# M003: Production deploy recovery and refactor hardening

**Gathered:** 2026-05-06
**Status:** Ready for planning

## Project Description

HappyGymStats is a refactored Torn gym statistics system with a Blazor frontend, ASP.NET API, shared Core/Data/Contracts projects, PostgreSQL-backed persistence, Keycloak-backed identity, an AdminPanel service, nginx routing, systemd services, and deployment scripts for the production VPS.

M003 recovers and hardens the production deployment after the refactor. The immediate visible failure is Blazor reporting `Failed to load surfaces data: Response status code does not indicate success: 502 (Bad Gateway)` when it tries to load backend surfaces data. The broader milestone is to make API, Blazor, AdminPanel, nginx, systemd, sudoers, Postgres, Keycloak, deployment scripts, smoke checks, and operator docs behave as one observable system instead of a set of loosely connected scripts and assumptions.

## Why This Milestone

The refactor left production with real operational gaps: the API may fail before serving endpoints if required production configuration is missing; Blazor currently surfaces backend failures as opaque 502 messages; AdminPanel has local service/sudoers artifacts but is not fully installed/enabled/routed on the server; deployment scripts restart services without proving they came back healthy; and docs still describe stale SQLite/static-frontend behavior.

This needs to happen now because the user-facing site is already broken at the Blazor-to-backend boundary, and future agents/operators cannot reliably fix production issues while the deployment contract, privileged setup, smoke proof, and docs are inconsistent.

## User-Visible Outcome

### When this milestone is complete, the user can:

- Open the Blazor surfaces page in the real deployment without seeing `502 Bad Gateway` from surfaces data calls.
- Run a single production smoke command that proves systemd units, nginx routes, API health, surfaces data, Blazor home/surfaces behavior, AdminPanel health/auth boundary, and Postgres/Keycloak container health.
- Deploy or re-deploy backend/frontend/AdminPanel changes with scripts that fail fast when required production prerequisites are missing instead of silently leaving a broken service behind.
- Use updated operator documentation that describes the current Blazor + API + Postgres + Keycloak deployment shape, not the stale SQLite/static-frontend shape.

### Entry point / environment

- Entry point: production deployment scripts and smoke verification commands; user-visible browser entry point at the Blazor site; API entry points under `/api/v1/torn/...`; AdminPanel health entry point at `/admin/health`.
- Environment: production-like VPS deployment using nginx, systemd services, local loopback ports, public HTTPS routing, and containerized Postgres/Keycloak.
- Live dependencies involved: nginx, systemd, Blazor server, ASP.NET API, AdminPanel service, PostgreSQL, Keycloak, sudoers, SSH/rsync-based deployment, and the surfaces cache directory.

## Completion Class

- Contract complete means: required production configuration and deployment contracts are explicit for API, Blazor, AdminPanel, sudoers, nginx, runtime/package assumptions, and docs; scripts and docs name the exact expected routes, ports, env vars, service names, and verification commands without committing secret values.
- Integration complete means: Blazor can load surfaces through the chosen API boundary; the API responds through both loopback and nginx health paths; AdminPanel health is reachable through the intended public route; protected AdminPanel APIs remain auth-gated; deploy scripts share configuration and execute machine-checkable preconditions.
- Operational complete means: production or production-like smoke verification proves service lifecycle behavior under real nginx/systemd/container conditions, including failure modes that local SQLite-only tests cannot catch.

## Final Integrated Acceptance

To call this milestone complete, we must prove:

- A full-stack smoke command verifies systemd units, nginx config/routes, API loopback health, API public nginx health, `/api/v1/torn/surfaces/latest`, Blazor home/surfaces behavior without 502, public AdminPanel `/admin/health`, protected AdminPanel auth behavior, and Postgres/Keycloak container health.
- Deployment scripts fail before or immediately after restart when required server prerequisites are absent: API env/connection string/signing key/cache directory, runtime/package assumptions, service files, nginx routes, sudoers permissions, and container health.
- The Blazor 502 class of failure is no longer opaque: diagnostics distinguish API down, nginx bad gateway/upstream failure, missing surfaces cache, import failure, and bad production configuration.
- What cannot be simulated if this milestone is truly done: the real or production-like nginx → systemd service → API/AdminPanel → Postgres/Keycloak lifecycle. Local unit tests and SQLite-only tests are useful but not sufficient proof.

## Architectural Decisions

### Server-side Blazor uses loopback API by default

**Decision:** In production, server-side Blazor should call the API through the internal loopback boundary by default, e.g. `http://127.0.0.1:5047`, while nginx `/api/` remains a public/proxy boundary that smoke verification also proves.

**Rationale:** The Blazor app uses server-side execution for the surfaces service path, so API calls originate on the VPS rather than directly in the browser. Calling the public host from the same server unnecessarily routes through public TLS/nginx/Cloudflare-style layers and makes upstream failures appear as opaque 502s. Loopback reduces fragility for server-side calls while still preserving the public nginx route as a separately verified external API boundary.

**Alternatives Considered:**
- Keep same-origin/public nginx URL for Blazor API calls — simpler mental model, but more fragile for server-side Blazor and currently contributes to opaque 502 behavior.
- Configurable dual path with loopback default plus public diagnostic mode — flexible, but increases configuration surface and test matrix; can be added later if needed.

---

### AdminPanel has public health and protected admin APIs

**Decision:** AdminPanel should be intentionally exposed through nginx with anonymous `/admin/health`, while admin APIs remain auth-gated and smoke verification proves both behaviors.

**Rationale:** A public anonymous health endpoint gives deployment and monitoring a safe, low-risk way to prove the AdminPanel route and service are alive. Protected admin APIs should continue to require the expected Keycloak/admin-role boundary, and M003 must verify that exposure does not accidentally make admin data public.

**Alternatives Considered:**
- Internal-only AdminPanel first — safer and smaller, but does not prove the intended operator access path or nginx exposure.
- Separate admin host/subdomain — cleaner long-term separation, but may require DNS/TLS work and is larger than the immediate recovery/hardening goal unless already available.

---

### Production mutations require explicit confirmation

**Decision:** Agents may prepare scripts, docs, tests, and local/static verification freely, but must ask for explicit confirmation before each production mutation class or approved operation batch: installing sudoers, installing/enabling systemd units, writing/reloading nginx config, restarting services, touching containers, or other outward-facing server changes.

**Rationale:** M003 necessarily touches privileged deployment surfaces. The user wants safe, auditable recovery rather than surprise remote mutations. Confirmation preserves control over production while still allowing the agent to do high-confidence preparation and verification work.

**Alternatives Considered:**
- One broad approval window — faster, but increases blast radius if a script or assumption is wrong.
- Dry-run only — safest, but cannot fully prove the reported production 502 recovery or full-stack smoke behavior.

---

### Completion requires one full-stack smoke proof

**Decision:** M003 completion requires a single smoke command or equivalent scripted proof across the assembled stack: systemd units, nginx config/routes, API health, surfaces endpoint, Blazor home/surfaces behavior, AdminPanel health/auth boundary, and Postgres/Keycloak container health.

**Rationale:** The failure class is operational and cross-boundary. Per-project tests and isolated deploy-script checks are not enough to prove that the real deployment works after restart/routing/configuration. A unified smoke command becomes the shared operational contract for future deploys and future agents.

**Alternatives Considered:**
- Deploy gates only — useful but still allows drift between scripts and misses whole-system behavior.
- Local proof only — insufficient for a production nginx/systemd/Postgres/Keycloak failure that manifests as 502.

---

### Preserve current .NET 10 / EF Core 10 direction, but verify it explicitly

**Decision:** M003 should verify the current .NET 10 / EF Core 10 runtime/package path rather than downgrading or deferring runtime policy, unless verification proves the current path cannot run safely on the server.

**Rationale:** The milestone is recovery and hardening, not a framework migration. The safest path is to make runtime and package assumptions explicit and machine-checkable so deploy fails early if the host cannot satisfy them. A downgrade could create broad code/test churn and distract from the immediate production recovery goal.

**Alternatives Considered:**
- Downgrade to a more conservative runtime — potentially lowers production novelty risk, but is too much scope for this recovery milestone unless forced by evidence.
- Defer runtime/package policy — faster, but leaves a known source of deploy surprises unguarded.

---

### Documentation becomes an operator guide, not just small patches

**Decision:** M003 should rewrite/update deployment-facing docs broadly enough that a new operator can understand, deploy, smoke-test, and troubleshoot the current Blazor + API + Postgres + Keycloak system.

**Rationale:** The docs are stale enough to be an operational risk. README/setup/deployment/overview/API examples that still imply SQLite or a static frontend can actively mislead future fixes for the 502 class of failure. The user chose an operator guide rewrite rather than minimal docs patching.

**Alternatives Considered:**
- Current-contract docs only — reasonable, but may leave the operator path fragmented.
- Minimal deployment docs patch — fastest, but leaves stale project docs in place for future confusion.

---

> See `.gsd/DECISIONS.md` for the full append-only register of all project decisions.

## Error Handling Strategy

M003 should make deployment/configuration failures explicit, early, and classifiable.

- API startup/configuration should fail fast with clear diagnostics when required production settings are absent or placeholders are still in use, without logging secret values.
- Deploy scripts should validate preconditions before publishing or restarting where possible, and should verify service health immediately after restart.
- Blazor surfaces errors should no longer display only raw `EnsureSuccessStatusCode` 502 text. UI/operator diagnostics should distinguish API down, nginx bad gateway/upstream failure, missing cache/404, import failure, and bad production configuration.
- AdminPanel setup should be idempotent and narrow: install service/sudoers/nginx artifacts safely, validate sudoers with `visudo`, validate nginx config before reload, and verify `/admin/health` after changes.
- Retry behavior should be bounded and purposeful: wait/poll briefly for service startup or cache artifact availability, but do not mask persistent failures with `|| true` or non-failing smoke checks.
- User-facing/operator messages should name the failing boundary and next check, not secrets or internal stack traces.

## Risks and Unknowns

- Actual 502 root cause has not yet been reproduced — M003 must prove whether the failure is API startup/config, nginx routing, cache path mismatch, Blazor base URL, database/container health, runtime mismatch, or another upstream condition.
- Privileged setup can become unsafe if sudoers is broadened too much — setup and steady-state permissions must be exact, narrow, and auditable.
- Public AdminPanel exposure could accidentally expose protected data — smoke verification must prove `/admin/health` is anonymous while admin APIs remain auth-gated.
- Runtime/package assumptions may not match the production server — .NET 10 / EF Core 10 must be verified explicitly before treating deploy as healthy.
- Local SQLite-heavy tests do not prove production Postgres startup/migration behavior — M003 needs production-provider integration coverage or smoke proof.
- Stale docs may cause future agents/operators to follow the wrong deployment model — documentation cleanup is part of the operational fix, not optional polish.
- Remote mutation requires user confirmation — execution plans must separate local preparation from server changes and ask before privileged operations.

## Existing Codebase / Prior Art

- `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md` — audit that identified the Blazor 502, AdminPanel deployment gap, stale docs, missing smoke gates, and runtime/package risks.
- `infra/nginx-torn.conf` — current nginx routing shape for public API and Blazor paths; must be validated and extended where needed.
- `infra/happygymstats-api.service` — API systemd service contract; currently needs explicit production env/cache/signing-key handling.
- `infra/happygymstats-blazor.service` — Blazor systemd service contract; relevant to the server-side API boundary.
- `infra/happygymstats-adminpanel.service` — local AdminPanel service file that must become installable/enabled on the server.
- `infra/sudoers-happygymstats` — local sudoers artifact; must be installed/validated safely and kept narrow.
- `scripts/deploy-backend.sh`, `scripts/deploy-frontend.sh`, `scripts/deploy-adminpanel.sh`, `scripts/deploy-config.sh` — deploy-script surface to normalize, gate, and make machine-checkable.
- `scripts/verify/s05-local-surfaces.sh` — useful local prior art for bounded startup polling and surfaces cache verification, but not enough for production nginx/systemd/container proof.
- `src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs` — API configuration resolution point for connection string and surfaces cache behavior.
- `src/HappyGymStats.Api/Program.cs` — API startup/migration/cache setup path; failures here can become nginx 502.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs` — central Blazor backend boundary where 502 diagnostics should improve.
- `src/HappyGymStats.AdminPanel/Program.cs` — AdminPanel auth posture and health behavior that deployment smoke must prove.
- `README.md`, `docs/OVERVIEW.md`, `docs/SETUP.md`, `docs/DEPLOYMENT.md`, `src/HappyGymStats.Api/HappyGymStats.Api.http` — known stale docs/examples to update into the current operator contract.

## Relevant Requirements

- Production Blazor surfaces must load through the intended API boundary without opaque 502 failures — M003 advances this by standardizing loopback server-side calls and public nginx verification.
- Deployment must fail fast on missing or invalid production prerequisites — M003 advances this through explicit env/service/runtime/smoke gates.
- AdminPanel must be installable, reachable, and safely auth-gated — M003 advances this through sudoers/systemd/nginx setup and health/auth smoke checks.
- Future operators/agents need accurate deployment knowledge — M003 advances this through the operator guide rewrite and updated API examples.

## Scope

### In Scope

- Define and verify the API production runtime contract: connection string, token signing key, surfaces cache directory, environment, listening URL, loopback health, and nginx health.
- Standardize Blazor production API base URL behavior on loopback for server-side calls, while keeping public nginx API route verification.
- Improve Blazor/backend diagnostics for API down, nginx bad gateway, missing cache, and import failure cases.
- Create/idempotently verify AdminPanel server setup for sudoers, systemd service installation/enabling, and loopback health.
- Add intentional AdminPanel nginx exposure with anonymous health and protected admin APIs.
- Build a full-stack production smoke command across services, nginx routes, API, surfaces, Blazor, AdminPanel, Postgres, and Keycloak.
- Normalize deployment scripts around shared config and machine-checkable preconditions.
- Add Postgres-backed integration coverage or equivalent production-provider proof for startup/migration/health/surfaces behavior.
- Rewrite operator-facing docs and API examples to match current Blazor + API + Postgres + Keycloak deployment shape.
- Verify .NET/runtime/package assumptions for reproducible deploys.

### Out of Scope / Non-Goals

- Do not commit or print secret values.
- Do not perform production sudoers/systemd/nginx/restart/container mutations without explicit user confirmation.
- Do not broaden sudoers with wildcard shell access or broad unrestricted commands.
- Do not treat local SQLite-only tests as complete proof of production behavior.
- Do not skip straight to cosmetic Blazor UI changes before proving the API/service/nginx production boundary.
- Do not downgrade the runtime/framework as part of M003 unless verification proves the current runtime path cannot be made safe.
- Do not leave documentation in a stale SQLite/static-frontend posture after the milestone.

## Technical Constraints

- Server-side Blazor calls run on the VPS; production API base URL should account for that and prefer loopback for the app-to-API boundary.
- Public nginx `/api/` route still needs independent verification because users/operators may hit it externally.
- API secrets/configuration must be supplied through production-safe mechanisms, not checked-in placeholders.
- Surfaces cache path must be explicit and aligned between API dynamic endpoints and any nginx static cache route that remains supported.
- AdminPanel service listens on loopback port `127.0.0.1:5048`; nginx exposure must intentionally proxy to it.
- Nginx config must be validated before reload.
- Sudoers installation must be validated with `visudo` and should distinguish one-time setup permissions from steady-state deploy permissions.
- Verification scripts must not hide failures with `|| true` or equivalent masking.
- When running dotnet verification scripts with fixed `ASPNETCORE_URLS`, use `--no-launch-profile` so launch profiles do not override the URL.

## Integration Points

- Blazor server — consumes surfaces/import endpoints and needs clear failure classification.
- ASP.NET API — serves health, surfaces latest, import behavior, migrations, cache writing, and configuration resolution.
- PostgreSQL — production data provider whose migrations/startup behavior must be proven beyond SQLite tests.
- Keycloak — AdminPanel auth provider and part of production container health.
- AdminPanel — separate service requiring systemd installation, nginx routing, public health, and protected APIs.
- nginx — public API route, Blazor route, AdminPanel route, config validation, reload behavior, and upstream 502 classification.
- systemd — service lifecycle for API, Blazor, and AdminPanel.
- sudoers — privileged deployment/setup boundary for rsync, install, service operations, nginx validation/reload, and related commands.
- Deployment scripts — backend/frontend/admin/container/config scripts must share settings, validate prerequisites, and call or align with smoke checks.
- Operator documentation — README/setup/deployment/overview/API examples become the durable contract for humans and future agents.

## Testing Requirements

Testing must cover local contracts, integration boundaries, and production-like lifecycle behavior.

- Unit/static tests should cover response/error classification and configuration parsing without exposing secrets.
- Integration tests should include a Postgres-backed provider path that applies migrations and exercises health/surfaces behavior, because SQLite-only tests do not catch the production startup failures that become nginx 502s.
- Deploy-script verification should check required env/config/service/runtime/package assumptions before restart where possible, and service health after restart.
- AdminPanel tests/smoke checks must prove `/admin/health` is anonymous while protected admin APIs remain auth-gated.
- Nginx tests must validate config and route behavior before reload/exposure.
- Full-stack smoke verification must run as a single operator command or clearly equivalent script and fail non-zero on any broken boundary.
- Documentation/API examples should be validated enough that stale routes like old `/v1/...` examples do not survive when current routes are `/api/v1/torn/...`.

## Acceptance Criteria

- S01/API production contract: API required env/config is documented and checked; service startup/health works on loopback and through nginx; missing config produces clear failure without secret leakage.
- S02/Blazor boundary: server-side Blazor uses the agreed loopback API boundary by default; surfaces load without 502; UI/operator diagnostics distinguish API down, nginx bad gateway, missing cache, and import failure.
- S03/AdminPanel setup: one-time setup safely installs/validates sudoers and systemd service artifacts, enables/starts AdminPanel, and proves loopback `/admin/health`.
- S04/AdminPanel routing: nginx exposes the intended AdminPanel health route; `/admin/health` is anonymous; admin APIs remain auth-gated; nginx config is validated before reload.
- S05/full-stack smoke: one production smoke command verifies systemd units, nginx routes, API health, surfaces latest, Blazor home/surfaces, AdminPanel health/auth boundary, and Postgres/Keycloak health.
- S06/deploy normalization: backend, frontend, admin, and container deploy scripts share configuration, avoid hardcoded SSH drift, use machine-checkable preconditions, and fail fast instead of leaving broken services.
- S07/Postgres integration: production-provider migration/startup/health/surfaces behavior is covered by Postgres-backed integration verification or an equivalent proof.
- S08/docs/operator guide: README, setup/deployment/overview docs, and API examples describe the current Blazor + API + Postgres + Keycloak shape and current `/api/v1/torn/...` routes.
- S09/runtime/package reproducibility: expected .NET runtime/SDK and package restore behavior are documented and verified; deploy fails early when assumptions are not met.

## Open Questions

- What exactly caused the current 502 in production — API startup/config, nginx upstream route, Blazor base URL, surfaces cache, database/container, runtime/package mismatch, or another boundary? Current thinking: M003 should not assume; it should instrument and smoke-test until this is proven.
- Should AdminPanel eventually move to a dedicated admin host/subdomain? Current thinking: not required for M003 unless existing DNS/TLS makes it cheap; public health plus protected admin APIs on the intended route is enough for recovery.
- Will the current .NET 10 / EF Core 10 path be available and stable on the production server? Current thinking: verify explicitly and only revisit runtime choice if verification fails.
- What production mutation batches will the user approve during execution? Current thinking: agents should prepare exact commands/scripts and ask before each privileged mutation or bounded operation batch.
