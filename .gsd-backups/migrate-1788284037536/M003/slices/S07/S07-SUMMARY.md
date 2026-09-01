---
id: S07
parent: M003
milestone: M003
provides:
  - Postgres-backed integration test tier for API startup, EF migrations, health, and surfaces endpoint behavior.
  - Canonical local/CI verifier for S07 provider coverage with explicit Docker skip and timeout semantics.
  - Clear test-tier naming that separates SQLite fast tests from production-provider parity checks.
requires:
  - slice: S01
    provides: Production-provider startup/migration assumptions and API health contract consumed by the Postgres integration tests.
affects:
  - S08
  - S09
key_files:
  - tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
  - tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
  - scripts/verify/s07-postgres-integration.sh
  - docs/SETUP.md
key_decisions:
  - Use Testcontainers.PostgreSql for the Postgres integration harness because the repository has no docker-compose file.
  - Keep SQLite tests as a fast tier but rename/annotate them so they do not imply production-provider parity.
  - Use filesystem fixture seeding for surfaces latest-cache success instead of invoking import orchestration or external Torn API calls.
  - Make Docker absence an explicit verifier SKIP and support intentional skip with HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1.
patterns_established:
  - Provider tests use a dedicated category/filter (`Category=PostgresApiIntegration`) and a canonical verifier script rather than being hidden inside the default fast test suite.
  - Provider failures are classified by phase (`[docker]`, `[startup]`, `[health]`, `[provider]`) so future deployment/startup regressions are easier to triage.
  - SQLite tests are explicitly scoped as fast endpoint/schema coverage, while Postgres tests own production-provider parity.
observability_surfaces:
  - `scripts/verify/s07-postgres-integration.sh` reports machine-readable PASS/SKIP/FAIL-style outcomes and Docker prerequisite diagnostics.
  - Postgres integration tests emit phase-specific diagnostic labels for Docker, startup/migration, health endpoint, and provider mismatch failures.
  - `/api/v1/torn/health` provider/status assertions serve as the runtime health signal for Npgsql startup coverage.
drill_down_paths:
  - .gsd/milestones/M003/slices/S07/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S07/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S07/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S07/tasks/T04-SUMMARY.md
  - .gsd/milestones/M003/slices/S07/tasks/T05-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-07T19:58:19.506Z
blocker_discovered: false
---

# S07: Postgres-backed integration coverage

**S07 added an explicit Postgres provider integration tier using Testcontainers, covering API startup health, EF migration/Npgsql wiring, surfaces missing-cache and present-cache behavior, and documented verifier/skip semantics for Docker-constrained environments.**

## What Happened

S07 converted production-provider assumptions from S01 into a dedicated test tier rather than relying on fast SQLite WebApplicationFactory coverage as the only signal. The slice chose Testcontainers.PostgreSql because this checkout has no infra/docker-compose.yml, added the required Postgres/Npgsql test dependencies, and documented the distinction between default fast tests and provider integration tests in docs/SETUP.md.

The core implementation is tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs. It builds a Postgres-backed WebApplicationFactory by replacing the API's EF registrations with UseNpgsql, applies the real EF migration/startup path, and then verifies /api/v1/torn/health returns ok with a PostgreSQL/Npgsql provider identity. The same provider tier covers /api/v1/torn/surfaces/latest for both the structured JSON 404 when latest.json is absent and a 200 JSON success path when a temporary latest.json cache fixture is present, avoiding any dependency on live Torn API calls.

The slice also clarified test naming: existing API endpoint and DbContext tests were renamed/annotated as SQLite-tier tests so they remain valuable for fast endpoint/schema coverage but no longer imply production-provider parity. A canonical verifier, scripts/verify/s07-postgres-integration.sh, now performs Docker CLI/daemon preflight checks, supports intentional skip through HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1, bounds provider execution with timeout support, and runs the Postgres category filter. Runtime test diagnostics use phase-specific markers such as [docker], [startup], [health], and [provider] to make provider failures attributable instead of surfacing as generic connection errors.

Fresh closer verification confirmed the S07-specific provider tier and documentation wiring. The full dotnet test suite still fails in this environment due to unrelated legacy dataset fixture path issues and an existing import-service shared-state test interaction outside the S07 provider tier; the closer could not repair source in this complete-slice unit because the tools policy restricted writes to .gsd only. Those failures are recorded as known limitations/follow-up rather than treated as evidence against the Postgres integration tier itself.

## Verification

Fresh closer verification was run via gsd_exec in this complete-slice unit.

Passed S07-specific checks:
- `dotnet restore` exited 0.
- Provider/docs grep found 7 Postgres/Testcontainers/integration references in `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` and `docs/SETUP.md`.
- `bash scripts/verify/s07-postgres-integration.sh` exited 0 with an explicit `SKIP: docker CLI not found; Postgres integration tests require Docker/Testcontainers` diagnostic, proving the verifier's Docker-unavailable path is actionable and non-ambiguous in this environment.
- `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1 dotnet test --filter "Category=PostgresApiIntegration" --logger "console;verbosity=minimal"` exited 0 with 3/3 tests passed, proving the category wiring compiles and intentional skip semantics work.
- Provider-scope grep found 83 SQLite/Postgres/Npgsql markers under `tests/HappyGymStats.Tests`, confirming test-tier naming/scope is explicit.

Non-passing broader check:
- `dotnet test --logger "console;verbosity=minimal"` exited 1 with 10 failures outside the S07 Postgres provider tier. Failures included legacy dataset consistency tests looking for fixture data under `src/HappyGymStats.Cli/bin/Debug/net8.0/data/...`, `DbPipelineIntegrationTests.Reconstruction_can_read_from_sqlite_when_legacy_jsonl_is_missing` using the same missing legacy fixture path, and `SqliteApiEndpointTests.Import_latest_returns_not_found_before_any_import` observing persisted ImportService latest state. These were already reported in T04/T05 summaries as unrelated full-suite failures and remain follow-up work for an execute-task unit.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

`infra/docker-compose.yml` was listed as a possible harness input but is absent in this checkout, so S07 selected Testcontainers. Docker is also unavailable in this execution environment, so closer verification proved Docker-unavailable and intentional-skip behavior rather than running a live PostgreSQL container. The close-slice tools policy prevented source edits to repair unrelated full-suite failures discovered during fresh verification.

## Known Limitations

Live PostgreSQL container execution was not proven in this closer environment because Docker CLI is unavailable; it is accepted through the explicit verifier skip contract and must be exercised on a Docker-enabled local/CI host. The broader `dotnet test` suite still has non-S07 failures involving legacy fixture data paths under the CLI build output and ImportService latest-state isolation in a SQLite endpoint test. These failures do not invalidate the Postgres provider tier but do prevent claiming full-suite health.

## Follow-ups

Create an execute-task remediation for full-suite failures: update legacy dataset consistency/DbPipeline fixture discovery to use the repository `data/` fixtures or copy them into build output, and isolate/reset ImportService latest state between SQLite API endpoint tests. S08 should document the new S07 test tiers, verifier command, skip switch, Docker prerequisite, and the fact that provider integration tests use test-only local Postgres rather than production secrets. A Docker-enabled CI lane should run `bash scripts/verify/s07-postgres-integration.sh` without the skip flag.

## Files Created/Modified

- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` — Adds explicit Postgres provider integration dependencies including Npgsql EF provider and Testcontainers.PostgreSql.
- `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs` — Adds Postgres-backed WebApplicationFactory integration tests for startup health and surfaces latest missing/present cache behavior with skip/timeout diagnostics.
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs` — Renames/annotates fast API endpoint tests as SQLite-tier coverage.
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` — Renames/annotates DbContext tests as SQLite-tier schema/constraint coverage.
- `scripts/verify/s07-postgres-integration.sh` — Introduces the canonical provider integration verifier with Docker preflight, intentional skip, timeout, and category-filter execution.
- `docs/SETUP.md` — Documents default fast tests versus Postgres provider integration tests, verifier usage, prerequisites, skip switch, and timeout tuning.
