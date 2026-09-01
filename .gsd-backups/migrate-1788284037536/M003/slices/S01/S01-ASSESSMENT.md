---
sliceId: S01
uatType: mixed
verdict: PASS
date: 2026-05-06T19:30:00Z
---

# UAT Result — S01

## Checks

| Check | Mode | Result | Notes |
|-------|------|--------|-------|
| Smoke test: run `bash scripts/verify/s01-api-production-contract.sh` | runtime | PASS | `gsd_exec` run `87e9616e-05e0-4ee5-a2c3-48861d30d4ac` exited 0. Output included `==> S01 verify passed`; targeted API endpoint tests inside the verifier passed 10/10. |
| Production runtime contract is declared and grep-able | artifact | PASS | `rg -n 'HAPPYGYMSTATS_CONNECTION_STRING|ConnectionStrings__HappyGymStats|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|ASPNETCORE_URLS' infra/happygymstats-api.service scripts/deploy-backend.sh docs/DEPLOYMENT.md` exited 0. All required names appeared in systemd, deploy script, and docs; observed lines declare names only, not secret values. |
| Backend deploy scripts are syntactically valid | artifact | PASS | `bash -n scripts/deploy-backend.sh` and `bash -n scripts/deploy-config.sh` both exited 0. |
| Deploy health gates distinguish API and nginx failures | artifact | PASS | `rg -n 'health|is-active|systemctl|127.0.0.1:5047|torn.geromet.com/api/v1/torn/health|502' scripts/deploy-backend.sh scripts/deploy-config.sh` exited 0. Evidence showed `systemctl is-active`, bounded `systemctl status`, loopback health URL, external nginx health URL, and explicit `external_nginx_502` handling. |
| Surfaces cache readiness is distinct from API-down | artifact/runtime | PASS | `rg -n 'surfaces|latest.json|not_found|cache' scripts/deploy-backend.sh src/HappyGymStats.Api tests/HappyGymStats.Tests/ApiEndpointTests.cs` exited 0 and showed surfaces metadata/latest probes plus structured `"code":"not_found"` handling. `dotnet test --filter 'ApiEndpointTests'` exited 0 with 10 passed / 0 failed / 0 skipped. |
| Live deploy health gates prove loopback and nginx API reachability | human-follow-up | NEEDS-HUMAN | This environment does not have the configured production host, `/etc/happygymstats/api.env`, service manager access, Postgres/Keycloak containers, or deploy credentials. A human/operator should run the backend deploy path or equivalent health-gate phase on production and confirm `DEPLOY_HEALTH_OK` markers for `happygymstats-api`, loopback `http://127.0.0.1:5047/api/v1/torn/health`, external `https://torn.geromet.com/api/v1/torn/health`, and surfaces readiness; broken service, loopback, nginx 502, non-2xx, and missing surfaces cache should emit the documented categorized failures/warnings. |

## Overall Verdict

PASS — All automatable local artifact and runtime checks passed; the only remaining evidence is the production-host live deploy check, which requires operator access to the configured server and secrets.

## Notes

- Full command output is preserved at `/Project/.gsd/exec/87e9616e-05e0-4ee5-a2c3-48861d30d4ac.stdout`; stderr was empty.
- `dotnet test` emitted existing NU1903 warnings for `System.Security.Cryptography.Xml` 9.0.0 advisories in `HappyGymStats.Data.csproj`; these warnings did not fail S01 UAT and are unrelated to the API reachability/deploy-contract checks.
- Remote URL checks were not forced locally because the S01 verifier intentionally keeps remote production checks opt-in unless `S01_ALLOW_REMOTE_URL_CHECKS=1` is set.
