---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T04: Add Postgres and Keycloak container checks

Why: API startup and auth depend on Postgres and Keycloak containers, so smoke must report container state without requiring credentials.

Do:
1. Add Postgres container health/status check from docker compose or docker ps.
2. Add Keycloak container health/status or HTTP reachability check where available.
3. Categorize container access unavailable separately from unhealthy containers.
4. Avoid printing `.env` values or docker inspect env variables.
5. Keep checks optional if the target host intentionally does not expose docker to the deploy user, but make the warning explicit.

Done when: smoke can explain API/auth failures caused by container state or lack of container visibility.

## Inputs

- `infra/docker-compose.yml`
- `docs/v2-plan.md`

## Expected Output

- `scripts/verify/production-smoke.sh`

## Verification

bash -n scripts/verify/production-smoke.sh && rg -n "postgres|keycloak|docker compose|docker ps|container|healthy|unhealthy" scripts/verify/production-smoke.sh

## Observability Impact

Signals added/changed: container health phase.
How a future agent inspects this: smoke output and docker compose status.
Failure state exposed: Postgres unhealthy, Keycloak unhealthy, no docker access.
