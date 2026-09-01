# S07: Postgres-backed integration coverage — UAT

**Milestone:** M003
**Written:** 2026-05-07T19:58:19.506Z

# S07 UAT: Postgres-backed integration coverage

## UAT Type

Developer/CI acceptance for provider-backed API integration tests. This UAT verifies that the repository has a clear Postgres integration tier, a canonical verifier, actionable Docker skip behavior, and tests that exercise API startup health plus surfaces cache behavior through the production EF provider boundary.

## Preconditions

- Work from the repository root.
- .NET SDK 8.x is available.
- For full live-provider execution, Docker CLI and daemon are installed and usable by the current user.
- No production database, Torn API key, Keycloak secret, or deployment secret is required.

## Test Case 1: Default verifier is safe in Docker-constrained environments

1. Run `bash scripts/verify/s07-postgres-integration.sh` on a machine without Docker CLI/daemon.
2. Expected: command exits 0 and prints an explicit SKIP message naming Docker/Testcontainers as the missing prerequisite.
3. Expected: output does not include a generic Npgsql connection exception and does not require production secrets.

## Test Case 2: Intentional skip path compiles and selects the provider tier

1. Run `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1 dotnet test --filter "Category=PostgresApiIntegration"`.
2. Expected: test project builds successfully.
3. Expected: the Postgres integration tests complete successfully through their intentional skip path.
4. Expected: failure messages, if any, are about category/build wiring rather than missing production config.

## Test Case 3: Live Postgres provider startup and health path (Docker-enabled host)

1. Ensure Docker is installed and running.
2. Run `bash scripts/verify/s07-postgres-integration.sh` without `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION`.
3. Expected: Testcontainers starts a local PostgreSQL container with test-only credentials.
4. Expected: the API test host starts with Npgsql configuration and applies EF migrations.
5. Expected: `/api/v1/torn/health` returns `status = ok` and a database provider string identifying PostgreSQL/Npgsql.
6. Expected: failures include phase-specific context such as `[docker]`, `[startup]`, `[health]`, or `[provider]`.

## Test Case 4: Surfaces missing-cache behavior under Postgres

1. Run the live provider tier on a Docker-enabled host.
2. Expected: the missing-cache surfaces test calls `/api/v1/torn/surfaces/latest` with no `latest.json` in the isolated cache directory.
3. Expected: the endpoint returns HTTP 404 with the structured JSON error envelope and `error.code = not_found`.

## Test Case 5: Surfaces present-cache behavior under Postgres

1. Run the live provider tier on a Docker-enabled host.
2. Expected: the present-cache test writes a temporary `latest.json` fixture into the isolated surfaces cache directory.
3. Expected: `/api/v1/torn/surfaces/latest` returns HTTP 200 and JSON containing `generatedAtUtc` and `series`.
4. Expected: no Torn API call or production cache directory is required.

## Edge Cases

- If Docker startup is slow, set `HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS` to tune the bounded startup wait rather than allowing a silent hang.
- If a CI lane intentionally excludes Docker, set `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1` and still run the category filter to prove compile/category wiring.
- If the provider identity assertion fails, investigate EF service override registration before assuming nginx or deployment config is broken.

## Not Proven By This UAT

- Live production Postgres connectivity, production credentials, or remote deployment health.
- Performance/load behavior of migrations or surfaces reads under large datasets.
- Full-suite legacy dataset consistency; current broader `dotnet test` failures are outside the S07 Postgres provider tier and need separate execute-task remediation.
- S08 documentation completeness beyond the S07 setup guidance.
