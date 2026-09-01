# S03: API Internal Reconstruction Extension

**Goal:** Extend Core reconstruction to persist baseline modifier provenance for each derived gym train and mark unresolved faction/company dependencies so downstream API confidence scoring has deterministic inputs.
**Demo:** After this slice, import pipeline reconstructs baseline modifier evidence and flags unresolved faction/company owner dependencies.

## Must-Haves

- Reconstruction run writes one `ModifierProvenance` row per derived train for personal scope with `VerificationStatus='verified'` and normalized validity window.
- Reconstruction run writes unresolved `ModifierProvenance` rows for faction/company scopes when source dependencies are absent, each with stable machine-readable reason codes.
- Provenance rows are refreshed transactionally with derived train/event refresh so consumers never observe mixed generations.
- Regression tests prove both positive persistence and unresolved-diagnostic paths for reconstruction output.

## Proof Level

- This slice proves: integration

## Integration Closure

ReconstructionRunner composes extraction + timeline reconstruction with EF persistence by adding provenance writes in the same refresh transaction as `DerivedGymTrains`/`DerivedHappyEvents`; S04 can then consume `ModifierProvenance` directly for confidence scoring without adding new reconstruction hooks.

## Verification

- Runtime signals: persisted `ModifierProvenance.VerificationStatus` and `VerificationReasonCode` per `DerivedGymTrainLogId`.
- Inspection surfaces: integration tests querying SQLite via `HappyGymStatsDbContext` and existing DB inspection patterns in test suite.
- Failure visibility: unresolved reason-code drift or missing provenance rows fail deterministic assertions.
- Redaction constraints: provenance reasons/ids are operational identifiers only; no tokens/secrets are persisted.

## Tasks

- [x] **T01: Add provenance reconstruction model and reason-code contract** `est:45m`
  Define Core-side provenance output records and deterministic unresolved reason constants so reconstruction can emit personal/faction/company evidence states in a stable shape consumed by Data persistence and later API confidence scoring.
  - Files: `src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs`, `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`, `tests/HappyGymStats.Tests/HappyTimelineReconstructorBehaviorTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyTimelineReconstructorBehaviorTests"

- [x] **T02: Persist provenance rows during transactional reconstruction refresh** `est:1h`
  Wire `ReconstructionRunner` to materialize modifier provenance rows (personal verified baseline plus unresolved faction/company placeholders) and persist them in the same DB transaction that replaces derived trains/events.
  - Files: `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`, `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`, `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"

- [x] **T03: Add integration tests for provenance persistence and unresolved dependency diagnostics** `est:1h`
  Add/extend integration coverage to prove reconstruction writes provenance rows and unresolved faction/company reason codes, and that full test suite remains green.
  - Files: `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ModifierProvenanceSchemaTests" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Files Likely Touched

- src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs
- src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
- tests/HappyGymStats.Tests/HappyTimelineReconstructorBehaviorTests.cs
- src/HappyGymStats.Data/HappyGymStatsDbContext.cs
- src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
- tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
