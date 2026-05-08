---
estimated_steps: 7
estimated_files: 3
skills_used: []
---

# T04: Clarify SQLite test scope

Why: Existing SQLite tests are valuable, but names/assertions should not imply they prove production provider behavior.

Do:
1. Rename or comment SQLite-specific fixtures/tests where necessary.
2. Update assertions that hardcode `Sqlite` so they are scoped only to SQLite factory tests.
3. Ensure Postgres integration tests assert provider separately.
4. Avoid deleting fast tests unless redundant and covered elsewhere.

Done when: test output and file names make provider scope clear.

## Inputs

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`

## Expected Output

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`

## Verification

dotnet test && rg -n "Sqlite|SQLite|Postgres|PostgreSQL|Npgsql" tests/HappyGymStats.Tests

## Observability Impact

Signals added/changed: provider scope clarity in test names/assertions.
How a future agent inspects this: test names/output.
Failure state exposed: accidental reliance on SQLite-only behavior.
