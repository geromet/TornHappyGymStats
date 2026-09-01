# S01: S01 — UAT

**Milestone:** M001
**Written:** 2026-04-30T23:03:24.132Z

# S01: S01 — UAT

**Milestone:** M001
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice changes module ownership boundaries and verification guards rather than user-facing behavior; build/test and boundary assertions are the authoritative proof.

## Preconditions

- Repository is on S01-completed code with duplicate CLI runtime primitive files removed.
- .NET 8 SDK is installed.
- Test project and verification script are available (`tests/HappyGymStats.Tests`, `scripts/verify-s01.sh`).

## Smoke Test

Run `dotnet build` and confirm successful solution compilation with no ownership ambiguity or missing-type errors.

## Test Cases

### 1. Ownership boundary remains enforced via tests

1. Run `dotnet test --filter "FullyQualifiedName~ModuleOwnershipBoundariesTests"`.
2. Observe test output for canonical Core-file existence checks and duplicate CLI-file absence checks.
3. **Expected:** Test run passes (2/2) and reports no duplicate ownership paths.

### 2. Slice verification harness catches boundary drift and validates overall health

1. Run `bash scripts/verify-s01.sh`.
2. Confirm script stages execute in order: build, targeted ownership tests, static file-absence assertions, full test suite.
3. **Expected:** Script exits 0 and prints S01 ownership boundary verification success.

## Edge Cases

### Duplicate primitive reintroduced under CLI tree

1. Recreate one removed file path (for example `src/HappyGymStats/Fetch/LogFetcher.cs`) with any placeholder implementation.
2. Re-run `bash scripts/verify-s01.sh`.
3. **Expected:** Script fails non-zero with an explicit offender-path message from static assertions and/or boundary tests.

## Failure Signals

- `dotnet build` reports ambiguous type ownership or missing symbols for fetch/reconstruction/storage primitives.
- `ModuleOwnershipBoundariesTests` fails on missing canonical Core files or present duplicate CLI files.
- `scripts/verify-s01.sh` exits non-zero and names offending duplicate paths.

## Not Proven By This UAT

- Durable import/reconstruction run-state persistence across API restarts (S02 scope).
- Transactional derived dataset refresh behavior and empty-window elimination during reconstruction (S03 scope).

## Notes for Tester

This slice is intentionally architectural; expected runtime behavior for CLI/API commands should remain unchanged. The primary acceptance signal is deterministic enforcement of Core-only ownership and clear diagnostics if that invariant regresses.
