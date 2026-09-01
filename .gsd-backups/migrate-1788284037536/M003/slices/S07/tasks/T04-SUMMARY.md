---
id: T04
parent: S07
milestone: M003
key_files:
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-07T19:51:19.149Z
blocker_discovered: false
---

# T04: Renamed and annotated SQLite-tier tests to make provider scope explicit while preserving separate Postgres provider assertions.

**Renamed and annotated SQLite-tier tests to make provider scope explicit while preserving separate Postgres provider assertions.**

## What Happened

Updated `tests/HappyGymStats.Tests/ApiEndpointTests.cs` to `SqliteApiEndpointTests`, renamed its fixture to `SqliteTestApplicationFactory`, and added SQLite-scoped class/test metadata plus an inline comment clarifying that production-provider parity is covered by Postgres integration tests. Updated `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` to `SqliteHappyGymStatsDbContextTests` with SQLite-tier XML docs and SQLite-prefixed test names for schema/constraint assertions. Kept all fast SQLite tests intact (no deletions) and retained existing Postgres integration provider assertions in `PostgresApiIntegrationTests` as the production-provider signal.

## Verification

Ran the task-plan verification commands. `dotnet test` executed and failed due to pre-existing `DbPipelineIntegrationTests` failures outside this task’s naming/scope changes; provider-scope grep passed and shows explicit SQLite/Postgres/Npgsql scope markers in test files and test names.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test` | 1 | ❌ fail | 6143ms |
| 2 | `rg -n "Sqlite|SQLite|Postgres|PostgreSQL|Npgsql" tests/HappyGymStats.Tests` | 0 | ✅ pass | 14ms |

## Deviations

None.

## Known Issues

`dotnet test` currently fails in existing `DbPipelineIntegrationTests` (10 failures) unrelated to the T04 provider-scope renames.

## Files Created/Modified

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`
