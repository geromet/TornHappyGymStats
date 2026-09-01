---
id: T01
parent: S03
milestone: M001
key_files:
  - src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
key_decisions:
  - Introduced a constructor-level `beforeDerivedInsert` test seam instead of production branching to keep runtime behavior unchanged while enabling deterministic rollback testing.
duration: 
verification_result: passed
completed_at: 2026-04-30T23:13:51.302Z
blocker_discovered: false
---

# T01: Wrapped derived-table refresh in a single transaction and added an injected-failure integration test proving rollback preserves last-good derived rows.

**Wrapped derived-table refresh in a single transaction and added an injected-failure integration test proving rollback preserves last-good derived rows.**

## What Happened

Updated `ReconstructionRunner` to execute the derived-table clear + repopulate sequence inside a single database transaction (`BeginTransaction`/`Commit`) so readers cannot observe a partial refresh state. Added a deterministic failure seam (`beforeDerivedInsert` callback) and used it in `DbPipelineIntegrationTests` to throw after clear but before insert. The new test first establishes a known-good derived dataset, then runs a failing refresh and verifies derived tables still contain rows afterward, demonstrating all-or-nothing behavior.

## Verification

Ran the slice task verification command for DbPipeline integration tests. All filtered tests passed, including the new rollback test case.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"` | 0 | ✅ pass | 1000ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
