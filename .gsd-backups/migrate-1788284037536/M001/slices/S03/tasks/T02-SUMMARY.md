---
id: T02
parent: S03
milestone: M001
key_files:
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
key_decisions:
  - Assert endpoint stability by comparing pre-failure vs post-failure item identities instead of relying on fixed row cardinality, reducing flakiness while preserving contract intent.
duration: 
verification_result: mixed
completed_at: 2026-04-30T23:16:07.189Z
blocker_discovered: false
---

# T02: Added API integration regression coverage proving read endpoints retain last-good derived dataset rows when a reconstruction refresh fails mid-transaction.

**Added API integration regression coverage proving read endpoints retain last-good derived dataset rows when a reconstruction refresh fails mid-transaction.**

## What Happened

Extended `tests/HappyGymStats.Tests/ApiEndpointTests.cs` with `Read_endpoints_keep_last_good_rows_when_reconstruction_refresh_fails`. The test seeds raw logs into SQLite, runs an initial successful reconstruction to establish last-good derived rows, captures `/v1/gym-trains` and `/v1/happy-events` responses, then triggers an injected failure via `ReconstructionRunner(beforeDerivedInsert: ...)`. It finally re-reads both endpoints through the API and asserts returned item identities match the pre-failure baseline, validating no empty/partial window is exposed at the consumer boundary.

## Verification

Ran the task verification command for API and DB pipeline integration suites. All filtered tests passed, including the new API rollback regression coverage and existing reconstruction rollback integration tests.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"` | 1 | ❌ fail | 3000ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"` | 0 | ✅ pass | 3000ms |

## Deviations

Adjusted assertion strategy from non-empty checks to pre/post identity equality on endpoint payloads after observing one endpoint may validly be empty for a minimal seed dataset; this strengthens the no-empty-window contract verification.

## Known Issues

None.

## Files Created/Modified

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
