# S01: Prove API reachability and production config — UAT

**Milestone:** M003
**Written:** 2026-05-06T19:23:23.781Z

# S01: Prove API reachability and production config — UAT

**Milestone:** M003  
**Written:** 2026-05-06

## UAT Type

- UAT mode: mixed
- Why this mode is sufficient: S01 is primarily an operational/deployment slice. Local artifact-driven verification proves the contract, scripts, route anchors, and endpoint tests without requiring secrets. Live-runtime acceptance remains available through the same deploy gates when production/server access is present.

## Preconditions

- Repository checkout contains S01 changes.
- .NET SDK/runtime compatible with the project is installed.
- No production secrets are required for local UAT.
- For live production checks only: `/etc/happygymstats/api.env` exists on the server with the required env names, Postgres/Keycloak containers are available, nginx is configured for `torn.geromet.com`, and the API service is managed by `happygymstats-api`.

## Smoke Test

Run:

```bash
bash scripts/verify/s01-api-production-contract.sh
```

**Expected:** The command exits 0 and prints `==> S01 verify passed`. Targeted `ApiEndpointTests` pass, deploy scripts parse, docs anchors exist, route anchors exist, and no remote network checks run unless explicitly enabled.

## Test Cases

### 1. Production runtime contract is declared and grep-able

1. Inspect `infra/happygymstats-api.service`, `scripts/deploy-backend.sh`, and `docs/DEPLOYMENT.md` for the required env names.
2. Run:
   ```bash
   rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ConnectionStrings__HappyGymStats|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|ASPNETCORE_URLS" infra/happygymstats-api.service scripts/deploy-backend.sh docs/DEPLOYMENT.md
   ```
3. **Expected:** All required contract terms are present by name; no secret values are printed or committed.

### 2. Backend deploy scripts are syntactically valid

1. Run:
   ```bash
   bash -n scripts/deploy-backend.sh
   bash -n scripts/deploy-config.sh
   ```
2. **Expected:** Both commands exit 0.

### 3. Deploy health gates distinguish API and nginx failures

1. Run:
   ```bash
   rg -n "health|is-active|systemctl|127.0.0.1:5047|torn.geromet.com/api/v1/torn/health|502" scripts/deploy-backend.sh scripts/deploy-config.sh
   ```
2. **Expected:** Output shows `systemctl is-active`/status handling, loopback health URL, external nginx health URL, health-gate configuration, and explicit 502 handling.

### 4. Surfaces cache readiness is distinct from API-down

1. Run:
   ```bash
   rg -n "surfaces|latest.json|not_found|cache" scripts/deploy-backend.sh src/HappyGymStats.Api tests/HappyGymStats.Tests/ApiEndpointTests.cs
   dotnet test --filter "ApiEndpointTests"
   ```
2. **Expected:** Grep output shows surfaces cache diagnostics and structured `not_found` handling. Tests pass and assert structured 404 envelopes for missing surfaces artifacts rather than treating the API as unavailable.

### 5. Live deploy health gates prove loopback and nginx API reachability

1. On a configured production host, run the backend deploy path with health gates enabled, or run the equivalent health-gate phase after restart.
2. Confirm the service check runs against `happygymstats-api`.
3. Confirm loopback health checks `http://127.0.0.1:5047/api/v1/torn/health`.
4. Confirm external health checks `https://torn.geromet.com/api/v1/torn/health`.
5. **Expected:** Healthy deployment emits `DEPLOY_HEALTH_OK` markers. Broken service emits service-not-active/status context. Broken loopback emits loopback failure. Nginx bad gateway emits the dedicated external 502 category. Non-2xx responses include status and bounded safe body excerpts.

## Edge Cases

- Missing `/etc/happygymstats/api.env` should fail before publish/restart with a `DEPLOY_PRECHECK_FAIL` marker.
- Missing required env key names should fail precheck without printing secret values.
- API service inactive/failed after restart should fail deployment and include bounded `systemctl status` output.
- Loopback port not listening should be categorized separately from nginx 502.
- `/api/v1/torn/surfaces/latest` returning structured `404 not_found` should be a cache readiness warning, not a generic API-down failure.
- Remote URL checks in the local verifier should remain skipped unless `S01_ALLOW_REMOTE_URL_CHECKS=1` is set.

## Not Proven By This UAT

- It does not prove the production server currently has correct secret values; it proves required env names and failure behavior.
- It does not prove Blazor loads surfaces through the production API boundary; that is S02.
- It does not prove the full-stack smoke command across AdminPanel, containers, nginx, and Blazor; that is S05.
- It does not prove Postgres migration/startup behavior in a real integration test harness; that is S07.

## Acceptance

S01 is accepted when the local verifier passes and, in a live deployment environment, backend deploy health gates provide explicit pass/fail categories for service state, loopback API health, external nginx API health, database-degraded health responses, and surfaces cache readiness without leaking secrets.
