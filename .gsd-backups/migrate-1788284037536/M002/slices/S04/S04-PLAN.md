# S04: Accuracy Scoring & Surface Payload

**Goal:** Map persisted modifier provenance records into deterministic per-point confidence values and reason codes, then surface them on `/api/v1/torn/surfaces/latest` so downstream UI can render red→green gradients with diagnostic context.
**Demo:** After this slice, /api/v1/torn/surfaces/latest includes per-point confidence values and reason codes supporting red→green gradients.

## Must-Haves

- 1) `/api/v1/torn/surfaces/latest` gym points include confidence payload fields derived from persisted `ModifierProvenance` rows for the same `DerivedGymTrainLogId`.
- 2) Confidence and reason semantics are deterministic: verified personal/faction/company evidence increases confidence, unresolved/missing evidence lowers confidence and emits stable reason codes.
- 3) API behavior is regression-tested for both complete and unresolved provenance cases and retains existing series compatibility for consumers.

## Proof Level

- This slice proves: This slice proves: integration
Real runtime required: yes
Human/UAT required: no

## Integration Closure

Upstream surfaces consumed: `src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs`, `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`, `src/HappyGymStats.Api/SurfacesCacheWriter.cs`, `src/HappyGymStats.Api/Program.cs`.
New wiring introduced in this slice: `SurfacesCacheWriter` joins derived train points to `ModifierProvenance` and writes confidence arrays/reason metadata into `latest.json` served by `/api/v1/torn/surfaces/latest`.
What remains before the milestone is truly usable end-to-end: S05 UI gradient/tooltips consumption and S06 operator acquisition workflow for unresolved ownership data.

## Verification

- Runtime signals: persisted reason-code distribution is reflected in emitted surface payload confidence metadata.
- Inspection surfaces: `/api/v1/torn/surfaces/latest`, cache artifact `web/data/surfaces/latest.json` (or configured cache dir), integration tests asserting unresolved reason visibility.
- Failure visibility: missing provenance joins surface explicit fallback reason codes in payload and failing integration assertions.
- Redaction constraints: no secrets/PII added; only existing log/scoring metadata is emitted.

## Tasks

- [x] **T01: Implement deterministic confidence projection in surfaces cache generation** `est:1h`
  Add backend projection logic that reads `ModifierProvenance` per derived train and computes a stable confidence score plus reason-code set for each gym point emitted to surfaces payload. Preserve existing series shape while adding confidence metadata as additive fields, and codify scoring rules directly from persisted verification status/reason semantics established in S03.
  - Files: `src/HappyGymStats.Api/SurfacesCacheWriter.cs`, `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs`, `src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs`, `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~Surface|FullyQualifiedName~Surfaces|FullyQualifiedName~DbPipelineIntegrationTests"

- [x] **T02: Add integration tests for surfaces confidence payload contract and failure semantics** `est:1h`
  Create/extend tests that execute import/reconstruction/cache-write flow and assert `/api/v1/torn/surfaces/latest`-compatible JSON includes confidence values and stable reason codes for both verified and unresolved provenance scenarios. Ensure contract remains deterministic and additive for existing consumers.
  - Files: `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `src/HappyGymStats.Api/SurfacesCacheWriter.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Files Likely Touched

- src/HappyGymStats.Api/SurfacesCacheWriter.cs
- src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs
- src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs
- src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
