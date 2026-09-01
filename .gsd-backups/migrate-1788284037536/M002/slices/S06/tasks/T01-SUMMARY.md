---
id: T01
parent: S06
milestone: M002
key_files:
  - src/HappyGymStats.Api/SurfacesCacheWriter.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-01T21:49:44.519Z
blocker_discovered: false
---

# T01: Added deterministic unresolved provenance warning projection to surfaces cache payload with reason-scoped diagnostics and integration coverage for malformed-row handling.

**Added deterministic unresolved provenance warning projection to surfaces cache payload with reason-scoped diagnostics and integration coverage for malformed-row handling.**

## What Happened

Implemented additive warning projection in `SurfacesCacheWriter` by extending provenance reads with subject/faction/company IDs, grouping unresolved/unavailable rows by `(logId, scope, status, reason, linkTarget)`, ordering deterministically, and bounding warning fanout per-log for 10x safety. Added `series.gymCloud.provenanceWarnings` plus `meta.provenanceWarningsDiagnostics` so operators and future agents can distinguish empty warnings from skipped malformed rows. Preserved confidence semantics and existing payload fields unchanged. Extended `DbPipelineIntegrationTests` to assert warning cardinality, ordering semantics, fallback-empty behavior, and malformed provenance row skipping via SQLite check-constraint bypass for negative-path coverage.

## Verification

Ran the task verification test filter and confirmed all targeted integration/confidence tests pass. Ran the artifact grep check command (non-failing optional check) as specified.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"` | 0 | ✅ pass | 9000ms |
| 2 | `grep -q "provenanceWarnings" web/data/surfaces/latest.json || true` | 0 | ✅ pass | 100ms |

## Deviations

Kept warning item JSON field names as serialized .NET record property names (`LogId`, `Scope`, etc.) to stay backward-safe with existing serializer defaults while still making the warning collection additive and machine-readable.

## Known Issues

`DbPipelineIntegrationTests` now emits existing nullable warnings in raw SQL insert parameter arrays for malformed-row simulation; these are compile warnings only and do not affect runtime behavior.

## Files Created/Modified

- `src/HappyGymStats.Api/SurfacesCacheWriter.cs`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
