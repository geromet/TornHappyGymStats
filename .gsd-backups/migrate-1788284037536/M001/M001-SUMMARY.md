---
id: M001
title: "Core/API Decoupling and DB-Native Pipeline Hardening"
status: complete
completed_at: 2026-04-30T23:45:34.421Z
key_decisions:
  - Core owns runtime fetch/reconstruction/path/checkpoint primitives; duplicates are removed and guarded by boundary tests/scripts.
  - Import lifecycle state is persisted in ImportRuns and served through durable latest/by-id status endpoints.
  - Derived-table refresh is transactional all-or-nothing to preserve last-good reads on failure.
  - Performance verification is artifact-first with deterministic fixtures and script-enforced benchmark artifact integrity.
  - DB-native parity is defined by durable DB state and endpoint coherence, not legacy CLI export comparisons.
key_files:
  - src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
  - src/HappyGymStats.Api/ImportService.cs
  - src/HappyGymStats.Api/Program.cs
  - tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
  - tests/HappyGymStats.Tests/Performance/ReconstructionPerformanceBenchmarkTests.cs
  - tests/HappyGymStats.Tests/ExportedDatasetConsistencyTests.cs
  - scripts/verify-s01.sh
  - scripts/verify-s04-benchmark.sh
  - docs/performance/reconstruction-baseline.md
  - README.md
  - src/HappyGymStats.Api/HappyGymStats.Api.http
lessons_learned:
  - Boundary-consolidation is safer when deletions are paired with explicit absence assertions and a single executable verification harness.
  - Restart-safe API contracts should be tested against durable identity/lifecycle progression, not a single transient status snapshot.
  - Transactional refresh plus deterministic failure seams gives high-confidence proof of no-empty-window guarantees for consumers.
  - Benchmarking becomes operationally useful when artifacts are machine-readable and validated by script, not only human-read logs.
  - Docs drift is best controlled by pairing narrative README statements with executable request examples and marker checks.
---

# M001: Core/API Decoupling and DB-Native Pipeline Hardening

**Delivered a DB-native, restart-safe import→reconstruct→read pipeline with Core-owned runtime primitives, atomic derived-data refresh, executable parity/performance verification, and aligned public API documentation.**

## What Happened

M001 unified runtime ownership by removing CLI-local pipeline primitive duplicates and establishing Core as the sole owner of fetch/reconstruction/path/checkpoint components, guarded by explicit boundary tests and a dedicated verification script. It then hardened operational correctness by persisting import lifecycle state in SQLite ImportRuns and exposing durable status retrieval through /v1/import/latest and /v1/import/{id}, including restart-boundary validation and 404 not_found behavior for unknown IDs. Reconstruction consistency was strengthened by moving derived-table clear+insert into a single transaction with deterministic failure injection tests proving rollback preserves last-good committed data for API readers. The milestone added deterministic large-fixture benchmarking with machine-readable artifacts and a scripted artifact-integrity verifier to establish a reproducible performance baseline contract. Finally, legacy export-parity assumptions were replaced with DB-native end-to-end parity tests spanning durable status and reconstructed read-model coherence, and README/.http docs were updated to accurately reflect the anonymous aggregate DB-native contract, lifecycle nuances, and deferred security hardening boundaries.

## Success Criteria Results

- ✅ **Core is the single source of truth for runtime pipeline primitives.** Evidence: S01 removed CLI-local duplicates for `LogFetcher`, `ReconstructionRunner`, `AppPaths`, and `Checkpoint`; `tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs` and `scripts/verify-s01.sh` enforce boundary invariants.
- ✅ **Import/reconstruction status is durable and restart-safe.** Evidence: S02 persisted lifecycle transitions in ImportRuns and validated `/v1/import/latest` and `/v1/import/{id}` across fresh app factories using file-backed SQLite (`ApiEndpointTests`).
- ✅ **Derived dataset writes are atomic and read-consistent.** Evidence: S03 implemented transactional derived refresh in `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`; integration and API tests verify rollback preserves last-good endpoint identities.
- ✅ **DB-native end-to-end tests cover import→reconstruct→read contract.** Evidence: S05 executed filtered suite (`ExportedDatasetConsistencyTests`, `DbPipelineIntegrationTests`, `ApiEndpointTests`) with pass results and no skips for targeted parity coverage.
- ✅ **Documentation matches implemented DB-native architecture and model.** Evidence: S06 aligned `README.md` and `src/HappyGymStats.Api/HappyGymStats.Api.http` with DB-native/no-auth contract and durable-status diagnostic flow; marker checks passed.

## Definition of Done Results

- ✅ All roadmap slices are complete: S01–S06 marked `[x]` in `M001-ROADMAP.md` and `gsd_milestone_status` reports all slice statuses as `complete`.
- ✅ All slice summaries exist: `find .gsd/milestones/M001/slices -name '*-SUMMARY.md'` returned each `S01`–`S06` summary (plus task summaries).
- ✅ Cross-slice integration closure achieved:
  - S01 ownership boundary enabled shared Core runtime for downstream API/test work.
  - S02 durable status surfaces were consumed and expanded by S05 parity tests and S06 docs.
  - S03 atomic refresh semantics were validated and carried into S05 endpoint coherence tests.
  - S04 benchmark artifact contract established performance-proof input for ongoing parity/optimization work.
  - S06 documentation now reflects behavior validated by S01–S05.
- ✅ Code-change verification passed: branch diff from `merge-base(HEAD, main)` to `HEAD` contains extensive non-`.gsd/` implementation and test/doc changes (e.g., `src/**`, `tests/**`, `README.md`, scripts).

## Requirement Outcomes

- No explicit requirement status transitions were recorded during M001 execution. Slice summaries consistently show requirement advancement/validation placeholders or none, and no requirement update artifacts were produced in this unit.
- Milestone-level contract outcomes are still evidenced by passing slice verification and integrated success-criteria proof above.

## Deviations

Minor planned-order deviations occurred within slices (not milestone-blocking): S02 wired /v1/import/{id} earlier within task flow; S03 endpoint assertions shifted to pre/post identity stability; S05 adjusted strict latest-status assumptions to lifecycle-aware expectations under hosted test timing.

## Follow-ups

Add operational observability for import/reconstruction lifecycle metrics and failure trends; add CI docs-contract drift checks mirroring S06 marker assertions; track benchmark trend snapshots over time instead of replacing baseline context; consider future hardening milestone for auth/CORS/write-surface protections and concurrent import race characterization.
