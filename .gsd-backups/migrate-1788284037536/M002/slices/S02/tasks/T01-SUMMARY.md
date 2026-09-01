---
id: T01
parent: S02
milestone: M002
key_files:
  - src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
  - src/HappyGymStats.Data/HappyGymStatsDbContext.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-01T20:51:29.867Z
blocker_discovered: false
---

# T01: Added a constrained ModifierProvenance EF model linked to derived train records with UTC interval persistence, scope/status validation, and DbContext coverage tests.

**Added a constrained ModifierProvenance EF model linked to derived train records with UTC interval persistence, scope/status validation, and DbContext coverage tests.**

## What Happened

Implemented `ModifierProvenanceEntity` as the canonical persistence contract for personal/faction/company provenance intervals, including verification lifecycle (`verified|unresolved|unavailable`) and machine-readable reason codes. Wired the new DbSet and full EF mapping into `HappyGymStatsDbContext` with required-field enforcement, scope/status check constraints, and indexes for `(Scope, ValidFromUtc, ValidToUtc)`, `(DerivedGymTrainLogId, Scope)` uniqueness, and status filtering. Added a foreign key from provenance rows to `DerivedGymTrainEntity.LogId` for the S03 derived-train identity surface. Preserved UTC interval semantics through existing DateTimeOffset converters. Extended DbContext tests to verify schema creation includes the new table, malformed status/identifier inserts fail, and adjacent windows (`end == next start`) persist without overlap corruption.

## Verification

Ran full solution build and targeted DbContext tests. Build compiles cleanly with zero warnings/errors after updating check-constraint mapping to the non-obsolete `ToTable(...HasCheckConstraint...)` pattern. DbContext test filter passed all 4 tests, including new negative and boundary cases for provenance constraints.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `dotnet build HappyGymStats.sln` | 0 | ✅ pass | 3280ms |
| 2 | `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"` | 0 | ✅ pass | 999ms |

## Deviations

None.

## Known Issues

None.

## Files Created/Modified

- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`
