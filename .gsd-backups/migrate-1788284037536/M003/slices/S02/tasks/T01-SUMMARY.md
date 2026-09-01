---
id: T01
parent: S02
milestone: M003
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json
  - infra/happygymstats-blazor.service
  - docs/DEPLOYMENT.md
key_decisions:
  - Made ApiBaseUrl a required config key at startup for Blazor host (fail-fast if missing).
  - Set production Blazor API boundary to direct loopback (http://127.0.0.1:5047) instead of public domain routing.
duration: 
verification_result: mixed
completed_at: 2026-05-06T19:27:44.105Z
blocker_discovered: false
---

# T01: Standardized Blazor’s production API boundary to explicit loopback (127.0.0.1:5047), removed localhost fallback, and documented server-side API call semantics for prod vs development.

**Standardized Blazor’s production API boundary to explicit loopback (127.0.0.1:5047), removed localhost fallback, and documented server-side API call semantics for prod vs development.**

## What Happened

I reviewed the Blazor host startup config, appsettings, nginx route shape, S01 deploy contract, and deployment docs to align the server-side API boundary with production semantics. Program.cs now requires ApiBaseUrl explicitly and no longer falls back to https://localhost:7001, with an inline note that server-side Blazor HttpClient runs on the host process and should use loopback in production. I changed Blazor production appsettings ApiBaseUrl to http://127.0.0.1:5047, added the same explicit value to infra/happygymstats-blazor.service, and extended docs/DEPLOYMENT.md with a dedicated Blazor runtime API boundary section that distinguishes production loopback from development local URL usage. This makes boundary drift visible via config/service/docs inspection and fail-fast startup behavior.

## Verification

Ran the task verification command scope (regex contract scan plus dotnet build). The first attempt failed only because /usr/bin/time is unavailable in this environment; reran with shell builtin timing and the verification passed. The grep output confirms ApiBaseUrl loopback appears in appsettings/service/docs, Program.cs no longer uses localhost fallback, and build succeeded with 0 errors (pre-existing NU1903 warnings remain unrelated to this task).

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `set -o pipefail; /usr/bin/time -f 'DURATION:%e' sh -c 'rg -n "ApiBaseUrl|127.0.0.1:5047|torn.geromet.com|localhost" src/HappyGymStats.Blazor/HappyGymStats.Blazor infra/happygymstats-blazor.service docs/DEPLOYMENT.md && dotnet build'` | 127 | ❌ fail | 1000ms |
| 2 | `set -o pipefail; TIMEFORMAT='DURATION:%3R'; time (rg -n "ApiBaseUrl|127.0.0.1:5047|torn.geromet.com|localhost" src/HappyGymStats.Blazor/HappyGymStats.Blazor infra/happygymstats-blazor.service docs/DEPLOYMENT.md && dotnet build)` | 0 | ✅ pass | 5532ms |

## Deviations

None.

## Known Issues

Pre-existing package vulnerability warnings (NU1903 on System.Security.Cryptography.Xml in HappyGymStats.Data) remain in dotnet build output and were not introduced by this task.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json`
- `infra/happygymstats-blazor.service`
- `docs/DEPLOYMENT.md`
