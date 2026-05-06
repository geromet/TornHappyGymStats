# S04: AdminPanel external routing and deploy smoke

**Goal:** Expose AdminPanel intentionally through nginx without weakening its authorization boundary. Decide host/path ownership and prove anonymous health vs protected admin APIs behave differently.
**Demo:** AdminPanel is reachable through its intended nginx host/path, health is public as intended, and protected endpoints remain auth-gated.

## Must-Haves

- AdminPanel nginx route is added or documented with a clear host/path decision and upstream `127.0.0.1:5048`.
- Nginx validation (`nginx -t`) and reload are included in the setup/deploy flow that owns the route.
- External AdminPanel health endpoint returns success without admin credentials, matching `AdminHealthController` intent.
- Protected AdminPanel endpoints reject unauthenticated/non-admin requests.
- Keycloak issuer/reachability assumptions are checked or documented for protected route verification.
- Deployment smoke includes both loopback and external AdminPanel health checks.
- Route does not shadow main Torn API/Blazor paths unintentionally.
- Verification commands do not mask nginx or route failures with `|| true`; optional local nginx absence must be reported explicitly by a verifier.

## Proof Level

- This slice proves: Nginx + AdminPanel integration proof. Real route verification is required for full proof; local/static validation is acceptable during implementation until production execution is explicitly confirmed.

## Integration Closure

Upstream surfaces consumed: S03 AdminPanel service on `127.0.0.1:5048`, `AdminHealthController`, protected AdminPanel controllers, Keycloak auth setup, nginx config patterns.
New wiring introduced: nginx AdminPanel route and route-level smoke checks.
What remains before milestone end-to-end: S05 must fold these checks into the full-stack smoke command.

## Verification

- Runtime signals: nginx config validation, AdminPanel external health, protected route unauthenticated rejection, upstream port/route failure category.
- Inspection surfaces: nginx config, `/admin/health`, protected `/admin/api/v1/...` response status, nginx reload output.
- Failure visibility: bad nginx syntax, upstream unavailable, route collision, unexpected public access to protected endpoints.
- Redaction constraints: auth headers/tokens must not be printed; protected checks should use unauthenticated negative assertions unless explicit credentials are securely provided later.

## Tasks

- [ ] **T01: Decide AdminPanel nginx route ownership** `est:45m`
  Why: The AdminPanel service has a port but no external route. A host/path decision needs to happen before writing nginx config and smoke checks.
  - Files: `docs/DEPLOYMENT.md`, `infra/nginx-torn.conf`, `infra/nginx-auth.conf`
  - Verify: rg -n "AdminPanel|admin\.geromet\.com|/admin|5048|auth-gated|health" docs/DEPLOYMENT.md

- [ ] **T02: Add AdminPanel nginx upstream config** `est:1.25h`
  Why: Once route ownership is chosen, nginx needs a concrete upstream config that preserves headers and does not break existing Blazor/API routes. The config verifier must fail on real config problems; it must not swallow nginx failures with `|| true`.
  - Files: `infra/nginx-adminpanel.conf`, `infra/nginx-torn.conf`, `infra/nginx-auth.conf`, `scripts/verify/s04-adminpanel-nginx-config.sh`
  - Verify: bash scripts/verify/s04-adminpanel-nginx-config.sh

- [ ] **T03: Install and validate AdminPanel nginx route** `est:1.5h`
  Why: Adding a local nginx file is not enough; setup/deploy needs to place it, validate config, and reload nginx safely.
  - Files: `scripts/setup-adminpanel-server.sh`, `infra/nginx-adminpanel.conf`, `docs/DEPLOYMENT.md`
  - Verify: bash -n scripts/setup-adminpanel-server.sh && rg -n "nginx -t|reload nginx|systemctl reload nginx|nginx-adminpanel|sites-enabled|conf.d|explicit user confirmation|remote setup" scripts/setup-adminpanel-server.sh docs/DEPLOYMENT.md

- [ ] **T04: Add AdminPanel route and auth smoke checks** `est:1h`
  Why: AdminPanel should expose anonymous health while protecting admin APIs. This boundary needs a smoke check before the full-stack script consumes it.
  - Files: `scripts/verify/s04-adminpanel-route.sh`, `docs/DEPLOYMENT.md`
  - Verify: bash scripts/verify/s04-adminpanel-route.sh

## Files Likely Touched

- docs/DEPLOYMENT.md
- infra/nginx-torn.conf
- infra/nginx-auth.conf
- infra/nginx-adminpanel.conf
- scripts/verify/s04-adminpanel-nginx-config.sh
- scripts/setup-adminpanel-server.sh
- scripts/verify/s04-adminpanel-route.sh
