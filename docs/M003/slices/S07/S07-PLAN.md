# S07: Postgres-backed integration coverage

**Goal:** Add production-provider test coverage for code paths most likely to cause API startup failures and nginx 502s. Keep fast SQLite tests if useful, but stop relying on them as the only provider signal.
**Demo:** Tests can run migrations against a real Postgres provider and hit health/surfaces paths, catching startup failures SQLite tests miss.

## Must-Haves

- A Postgres-backed integration test path exists using Testcontainers, docker compose, or an equivalent controlled provider.
- Test applies EF migrations against Postgres and verifies the API can start with Npgsql configuration.
- Test hits `/api/v1/torn/health` and validates provider/status semantics appropriate for Postgres.
- Test covers surfaces latest behavior for both missing cache (structured 404) and present cache where practical.
- Existing SQLite-specific tests are renamed/scoped so they do not imply production-provider coverage.
- CI/local instructions explain how to run or skip provider integration tests explicitly.
- Provider test failures produce actionable setup messages, not generic connection exceptions.

## Proof Level

- This slice proves: Automated integration proof against PostgreSQL provider. Must not require real production secrets.

## Integration Closure

Upstream surfaces consumed: S01 production provider assumptions, EF migrations, API startup path, health/surfaces controllers.
New wiring introduced: Postgres-backed integration test tier.
What remains before milestone end-to-end: S08 must document test tiers; S09 may add runtime/package preflight around provider test tooling.

## Verification

- Runtime signals: provider-specific test failures for migrations/startup/health/surfaces behavior.
- Inspection surfaces: test output, integration test skip/failure messages, CI/local docs.
- Failure visibility: Postgres migration failure, connection failure, provider mismatch, missing cache structured 404 regression.
- Redaction constraints: test connection strings must be local/test-only and never production secrets.

## Tasks

- [ ] **T01: Choose and wire Postgres test harness** `est:45m`
  Why: The test approach affects local developer experience and CI reliability. Testcontainers is ideal if acceptable; docker compose fallback may be simpler if the project already uses compose.
  - Files: `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `docs/SETUP.md`, `infra/docker-compose.yml`
  - Verify: dotnet restore && rg -n "Postgres|PostgreSQL|Testcontainers|docker compose|integration" tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj docs/SETUP.md

- [ ] **T02: Add Postgres API startup health test** `est:2h`
  Why: The production 502 can come from API startup failing during EF migration or DB connection; tests should exercise the real Npgsql startup path.
  - Files: `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
  - Verify: dotnet test --filter "PostgresApiIntegration"

- [ ] **T03: Cover surfaces endpoint under Postgres** `est:1.5h`
  Why: Surfaces endpoint behavior is directly tied to the reported frontend error and should be tested under the production provider too.
  - Files: `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs`, `src/HappyGymStats.Api/Controllers/SurfacesController.cs`
  - Verify: dotnet test --filter "PostgresApiIntegration"

- [ ] **T04: Clarify SQLite test scope** `est:45m`
  Why: Existing SQLite tests are valuable, but names/assertions should not imply they prove production provider behavior.
  - Files: `tests/HappyGymStats.Tests/ApiEndpointTests.cs`, `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`, `tests/HappyGymStats.Tests/SqlitePathsTests.cs`
  - Verify: dotnet test && rg -n "Sqlite|SQLite|Postgres|PostgreSQL|Npgsql" tests/HappyGymStats.Tests

- [ ] **T05: Add Postgres integration verifier and docs** `est:45m`
  Why: Provider tests often fail because Docker is unavailable or slow; the slice needs a clear command that future agents can run and understand.
  - Files: `scripts/verify/s07-postgres-integration.sh`, `docs/SETUP.md`
  - Verify: bash scripts/verify/s07-postgres-integration.sh

## Files Likely Touched

- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- docs/SETUP.md
- infra/docker-compose.yml
- tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs
- src/HappyGymStats.Api/Controllers/SurfacesController.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
- tests/HappyGymStats.Tests/SqlitePathsTests.cs
- scripts/verify/s07-postgres-integration.sh
