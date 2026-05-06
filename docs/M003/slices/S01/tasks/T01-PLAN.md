---
estimated_steps: 8
estimated_files: 5
skills_used: []
---

# T01: Declare API production environment contract

Why: The API currently has placeholder settings and service env that do not declare the production contract, so deployment can fail into an nginx 502 without clear precondition output.

Do:
1. Read the API startup/config path and current service/deploy scripts.
2. Decide the exact production env contract names without embedding secret values: `HAPPYGYMSTATS_CONNECTION_STRING` or `ConnectionStrings__HappyGymStats`, `ProvisionalToken__SigningKey`, `HAPPYGYMSTATS_SURFACES_CACHE_DIR`, `ASPNETCORE_ENVIRONMENT`, and `ASPNETCORE_URLS`.
3. Add an explicit non-secret contract surface in deployment docs/script comments/service env-file references so future deploys know where values must come from.
4. Align the surfaces cache directory expected by API with the nginx static alias if `/data/surfaces/` remains supported.
5. Make missing env errors actionable and grep-able without logging values.

Done when: a future agent can inspect the service/deploy files and know every required production env var, where it is loaded from, and what health failure it causes if absent.

## Inputs

- `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md`
- `infra/happygymstats-api.service`
- `scripts/deploy-backend.sh`
- `src/HappyGymStats.Api/appsettings.json`

## Expected Output

- `infra/happygymstats-api.service`
- `scripts/deploy-backend.sh`
- `docs/DEPLOYMENT.md`

## Verification

rg -n "HAPPYGYMSTATS_CONNECTION_STRING|ConnectionStrings__HappyGymStats|ProvisionalToken__SigningKey|HAPPYGYMSTATS_SURFACES_CACHE_DIR|ASPNETCORE_URLS" infra/happygymstats-api.service scripts/deploy-backend.sh docs/DEPLOYMENT.md && dotnet build

## Observability Impact

Signals added/changed: explicit precondition names and config failure messages.
How a future agent inspects this: read service/deploy contract and run backend deploy precheck.
Failure state exposed: missing required env/config before opaque service crash.
