---
estimated_steps: 7
estimated_files: 2
skills_used: []
---

# T05: Add Postgres integration verifier and docs

Why: Provider tests often fail because Docker is unavailable or slow; the slice needs a clear command that future agents can run and understand.

Do:
1. Add a S07 verifier that runs or intentionally skips Postgres integration tests with an explicit reason.
2. Document local/CI prerequisites and skip variable if needed.
3. Keep full `dotnet test` behavior sensible: provider tests should not hang indefinitely.
4. Include timeout guidance if the harness supports it.

Done when: provider coverage has a reliable command and documented failure/skip semantics.

## Inputs

- `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs`
- `docs/SETUP.md`

## Expected Output

- `scripts/verify/s07-postgres-integration.sh`
- `docs/SETUP.md`

## Verification

bash scripts/verify/s07-postgres-integration.sh

## Observability Impact

Signals added/changed: provider test verifier output and skip reason.
How a future agent inspects this: run S07 verifier.
Failure state exposed: Docker unavailable vs test failure vs timeout.
