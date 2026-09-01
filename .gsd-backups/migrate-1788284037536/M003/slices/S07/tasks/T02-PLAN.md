---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T02: Add Postgres API startup health test

Why: The production 502 can come from API startup failing during EF migration or DB connection; tests should exercise the real Npgsql startup path.

Do:
1. Add a test fixture that provisions a clean Postgres database.
2. Configure `WebApplicationFactory<Program>` to use the Postgres connection string.
3. Ensure migrations run as they do in production startup.
4. Call `/api/v1/torn/health` and assert status `ok` and provider contains Npgsql/PostgreSQL.
5. Make failure messages identify container/connection/migration/startup phase.

Done when: test suite has at least one API startup/health test backed by Postgres.

## Inputs

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `src/HappyGymStats.Api/Program.cs`
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs`

## Expected Output

- `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs`

## Verification

dotnet test --filter "PostgresApiIntegration"

## Observability Impact

Signals added/changed: provider startup/migration test output.
How a future agent inspects this: targeted Postgres integration test.
Failure state exposed: migration/startup/provider failure before deployment.
