---
estimated_steps: 8
estimated_files: 3
skills_used: []
---

# T01: Choose and wire Postgres test harness

Why: The test approach affects local developer experience and CI reliability. Testcontainers is ideal if acceptable; docker compose fallback may be simpler if the project already uses compose.

Do:
1. Evaluate current test packages, SDK target, and Docker availability assumptions.
2. Choose Testcontainers or compose-based Postgres tests.
3. Add package/reference only if needed and pin version explicitly.
4. Define skip behavior when Docker is unavailable.
5. Document the selected test tier name and invocation.

Done when: the provider integration strategy is explicit in tests/docs before adding assertions.

## Inputs

- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
- `infra/docker-compose.yml`
- `.gsd/milestones/M003/slices/S01/S01-SUMMARY.md`

## Expected Output

- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
- `docs/SETUP.md`

## Verification

dotnet restore && rg -n "Postgres|PostgreSQL|Testcontainers|docker compose|integration" tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj docs/SETUP.md

## Observability Impact

Signals added/changed: clear skip/setup messages for provider tests.
How a future agent inspects this: test project references and setup docs.
Failure state exposed: Docker unavailable vs Postgres test failed.
