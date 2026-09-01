# S03: Transactional derived dataset refresh

**Goal:** Make derived dataset refresh atomic so readers never observe an empty/partial derived table set during reconstruction refresh, including failure paths.
**Demo:** Derived data refresh no longer exposes an empty-table window during reconstruction.

## Must-Haves

- Reconstruction writes for `DerivedGymTrains` and `DerivedHappyEvents` are committed in one transaction.
- If refresh fails after delete intent but before commit, prior derived rows remain queryable.
- Integration tests prove both successful commit and rollback behavior using SQLite DB state assertions.

## Proof Level

- This slice proves: integration

## Integration Closure

Consumes existing DB-native reconstruction pipeline from Core and existing read endpoints from API. Adds transactional write boundary inside `ReconstructionRunner` without changing endpoint contracts.

## Verification

- Failure diagnosis remains DB-first: agents can inspect `ImportRuns` outcome/error and verify derived table row counts directly in SQLite during rollback scenarios.

## Tasks

- [x] **T01: Implement transactional derived dataset swap in ReconstructionRunner** `est:1h`
  Wrap derived-table refresh in a single database transaction so delete+insert is all-or-nothing. Add a deterministic test seam for injecting a failure between clear and insert to prove rollback preserves last-good data.
  - Files: `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"

- [x] **T02: Add API-level regression coverage for no-empty-window contract** `est:1h`
  Extend API integration coverage to assert read endpoints still return previously committed derived data when a reconstruction refresh attempt fails, matching the slice demo at consumer-facing boundary.
  - Files: `tests/HappyGymStats.Tests/ApiEndpointTests.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `src/HappyGymStats.Api/Program.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"

## Files Likely Touched

- src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- src/HappyGymStats.Api/Program.cs
