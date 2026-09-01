---
id: T01
parent: S04
milestone: M002
key_files:
  - src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs
  - src/HappyGymStats.Api/SurfacesCacheWriter.cs
  - tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-01T21:16:31.364Z
blocker_discovered: false
---

# T01: Added deterministic per-point confidence and reason-code projection from modifier provenance into surfaces cache payloads with fallback diagnostics.

**Added deterministic per-point confidence and reason-code projection from modifier provenance into surfaces cache payloads with fallback diagnostics.**

## What Happened

Implemented provenance-aware confidence projection in `SurfaceSeriesBuilder` and wired `SurfacesCacheWriter` to load and group `ModifierProvenance` rows per derived train log ID. The surfaces payload shape was preserved and extended additively by emitting `gymCloud.confidence` and `gymCloud.confidenceReasons` arrays aligned to existing gym point indices. Confidence scoring is deterministic: each provenance row contributes a fixed status multiplier (`verified=1.0`, `unresolved=0.75`, `unavailable=0.6`, unknown=0.5), then the product is clamped/rounded; reason codes are deduplicated and ordinal-sorted for stable output. Added fallback behavior for unmatched joins (`missing-provenance-record`) with low deterministic confidence so missing provenance is visible rather than silently omitted. Added focused tests covering projected score/reason ordering and fallback behavior.

## Verification

Ran the slice-required filtered test command and confirmed all targeted surface and DB pipeline integration tests pass, including new confidence-projection unit tests.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~Surface|FullyQualifiedName~Surfaces|FullyQualifiedName~DbPipelineIntegrationTests"` | 0 | ✅ pass | 2000ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs`
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs`
- `tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs`
