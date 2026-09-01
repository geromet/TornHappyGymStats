---
estimated_steps: 7
estimated_files: 2
skills_used: []
---

# T04: Update API HTTP examples

Why: `.http` examples are runnable documentation and currently use stale routes.

Do:
1. Update `src/HappyGymStats.Api/HappyGymStats.Api.http` to use `/api/v1/torn/...` routes.
2. Include health, surfaces meta/latest, import jobs latest, import start example, identity/faction examples where safe.
3. Avoid real API keys or tokens; use placeholders only.
4. Align host default with current local API port.

Done when: every `.http` request path matches actual controller routes or is clearly marked auth-required/placeholder.

## Inputs

- `src/HappyGymStats.Api/HappyGymStats.Api.http`
- `src/HappyGymStats.Api/Controllers/HealthController.cs`
- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Api/Controllers/SurfacesController.cs`

## Expected Output

- `src/HappyGymStats.Api/HappyGymStats.Api.http`

## Verification

rg -n "/api/v1/torn/health|/api/v1/torn/surfaces/latest|/api/v1/torn/import-jobs" src/HappyGymStats.Api/HappyGymStats.Api.http && ! rg -n "GET .* /v1/|POST .* /v1/|localhost:5047/v1" src/HappyGymStats.Api/HappyGymStats.Api.http

## Observability Impact

Signals added/changed: runnable route examples for health/surfaces/import.
How a future agent inspects this: execute `.http` requests or read routes.
Failure state exposed: stale route examples fail verifier.
