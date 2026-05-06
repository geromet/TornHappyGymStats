---
estimated_steps: 8
estimated_files: 5
skills_used: []
---

# T01: Standardize Blazor production API base URL

Why: Server-side Blazor currently uses `ApiBaseUrl` from appsettings, which points to the public site. The production boundary should be explicit so the app does not accidentally route through Cloudflare/nginx when loopback is intended.

Do:
1. Review `Program.cs`, Blazor appsettings, service files, and nginx routes.
2. Choose the production API base URL strategy based on server-side Blazor semantics: prefer loopback `http://127.0.0.1:5047` unless there is a specific reason to exercise public same-origin nginx.
3. Encode the choice in configuration/service/deploy docs without breaking local development.
4. Keep development `ApiBaseUrl` usable and documented.
5. Add comments or docs explaining why server-side Blazor calls differ from browser-origin calls.

Done when: production and development API base URLs are explicit, documented, and not reliant on fallback `https://localhost:7001`.

## Inputs

- `.gsd/milestones/M003/slices/S01/S01-SUMMARY.md`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs`
- `infra/nginx-torn.conf`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json`
- `infra/happygymstats-blazor.service`
- `docs/DEPLOYMENT.md`

## Verification

rg -n "ApiBaseUrl|127.0.0.1:5047|torn.geromet.com|localhost" src/HappyGymStats.Blazor/HappyGymStats.Blazor infra/happygymstats-blazor.service docs/DEPLOYMENT.md && dotnet build

## Observability Impact

Signals added/changed: clearer config values and deploy docs for API boundary.
How a future agent inspects this: read Blazor appsettings/service and deployment docs.
Failure state exposed: wrong/missing `ApiBaseUrl` becomes visible as config contract drift.
