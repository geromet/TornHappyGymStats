# S01: Finalize module ownership boundaries

**Goal:** Make HappyGymStats.Api and HappyGymStats CLI consume fetch/reconstruction/storage path primitives from shared Core modules only, with local duplicate implementations removed from the CLI project.
**Demo:** API and CLI both compile/run using shared Core primitives only, with duplicate implementations removed.

## Must-Haves

- API and CLI both compile and run with `LogFetcher`, `ReconstructionRunner`, `AppPaths`, and `Checkpoint` resolved from `src/HappyGymStats.Core/` (no duplicate type definitions under `src/HappyGymStats/Fetch`, `src/HappyGymStats/Reconstruction`, or `src/HappyGymStats/Storage/Models`).
- `dotnet build` and `dotnet test` pass after boundary consolidation.
- Ownership boundary guard test fails if duplicate runtime primitive sources are reintroduced outside Core.
- Verification script `scripts/verify-s01.sh` passes in CI/local and explicitly checks boundary invariants.

## Proof Level

- This slice proves: - This slice proves: integration
- Real runtime required: yes
- Human/UAT required: no

## Integration Closure

- Upstream surfaces consumed: `src/HappyGymStats.Core/*`, `src/HappyGymStats.Api/Program.cs`, `src/HappyGymStats/Program.cs`, existing test project wiring.
- New wiring introduced in this slice: CLI composition roots continue using same namespaces but now bind only to Core implementations after duplicate source removal.
- What remains before the milestone is truly usable end-to-end: nothing for ownership boundaries; durable status and transactional behavior remain in downstream slices.

## Verification

- Runtime signals: compiler/type-resolution failures and boundary tests become the primary diagnostic signal for ownership drift.
- Inspection surfaces: `dotnet build`, `dotnet test`, and `scripts/verify-s01.sh` output; boundary assertion test in `tests/HappyGymStats.Tests`.
- Failure visibility: test output names offending duplicate paths/types; build errors localize ambiguous type ownership.
- Redaction constraints: no secrets or PII in diagnostics.

## Tasks

- [x] **T01: Consolidate runtime primitive ownership into HappyGymStats.Core** `est:90m`
  ---
estimated_steps: 7
estimated_files: 8
skills_used:
  - tdd
  - verify-before-complete
---

# T01: Consolidate runtime primitive ownership into HappyGymStats.Core

**Slice:** S01 — Finalize module ownership boundaries
**Milestone:** M001

## Description

Remove duplicate runtime primitive implementations from the CLI project so only Core owns fetch/reconstruction/path primitives while preserving current CLI/API behavior and compile compatibility.

## Failure Modes

| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| .NET build graph (`HappyGymStats.csproj`, `HappyGymStats.Core.csproj`) | Restore source files from git and re-apply changes incrementally to isolate missing symbols or namespace drift. | Not applicable (local build step). | Fail fast on compiler ambiguity/missing-type errors; update usings/constructor signatures to match Core contracts. |
| Existing CLI command flows (`src/HappyGymStats/Program.cs`) | Keep command behavior unchanged; if runtime behavior changes, rollback and rewire only ownership boundaries. | Not applicable. | Add targeted unit/integration assertions in T02 before finalizing to catch regressions. |

## Load Profile

- **Shared resources**: Build/test runner process memory and project references.
- **Per-operation cost**: One full solution compile plus selective namespace/type rewiring.
- **10x breakpoint**: N/A for runtime load; primary risk is maintenance churn from duplicate code reintroduction.

## Negative Tests

- **Malformed inputs**: N/A (no new external input surface).
- **Error paths**: Compile with removed files to ensure no unresolved symbol fallbacks remain.
- **Boundary conditions**: Verify both API and CLI composition roots still resolve same namespaces after source removal.

## Steps

1. Inventory duplicate ownership targets (`LogFetcher`, `ReconstructionRunner`, `AppPaths`, `Checkpoint`) across `src/HappyGymStats` and `src/HappyGymStats.Core`.
2. Remove duplicate source files from `src/HappyGymStats/Fetch`, `src/HappyGymStats/Reconstruction`, and `src/HappyGymStats/Storage/Models` that are already provided by Core.
3. Update `src/HappyGymStats/Program.cs` usings/construction paths to align with remaining Core-owned implementations without changing feature behavior.
4. Validate API composition root (`src/HappyGymStats.Api/Program.cs`) still consumes shared primitives only and has no hidden local fallback.
5. Ensure project references remain minimal and explicit (`HappyGymStats` and API depend on Core/Data, not duplicate source ownership).
6. Run `dotnet build` and fix ownership-related compile errors only (avoid scope creep).
7. Capture resulting ownership boundary in comments or tests added in T02, not ad-hoc documentation.

## Must-Haves

- [ ] Duplicate runtime primitive implementations are removed from CLI-local source tree where Core already provides them.
- [ ] CLI and API compile against shared Core primitives with no ambiguous type resolution.

## Verification

- `dotnet build`
- `dotnet test --filter "FullyQualifiedName~ModuleOwnership|FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ApiEndpointTests"`

## Observability Impact

- Signals added/changed: compile failures now clearly indicate ownership regressions instead of silent duplicate divergence.
- How a future agent inspects this: run `dotnet build` and inspect type/file ownership in errors.
- Failure state exposed: duplicate ownership or unresolved dependency is visible as deterministic build/test failures.

## Inputs

- `.gsd/milestones/M001/M001-ROADMAP.md` — slice demo and ownership objective.
- `src/HappyGymStats/Program.cs` — CLI composition root currently consuming duplicated primitives.
- `src/HappyGymStats.Api/Program.cs` — API composition root boundary check.
- `src/HappyGymStats/Fetch/LogFetcher.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Storage/AppPaths.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Storage/Models/Checkpoint.cs` — duplicate implementation candidate.
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs` — canonical ownership target.

## Expected Output

- `src/HappyGymStats/Program.cs` — rewired to rely on Core-owned primitives only.
- `src/HappyGymStats/Fetch/LogFetcher.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Storage/AppPaths.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Storage/Models/Checkpoint.cs` — removed if duplicate of Core.
- `src/HappyGymStats/HappyGymStats.csproj` — updated only if compile-item ownership needs explicit adjustment.
  - Files: `src/HappyGymStats/Program.cs`, `src/HappyGymStats/Fetch/LogFetcher.cs`, `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs`, `src/HappyGymStats/Storage/AppPaths.cs`, `src/HappyGymStats/Storage/Models/Checkpoint.cs`, `src/HappyGymStats.Core/Fetch/LogFetcher.cs`, `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`, `src/HappyGymStats.Core/Storage/AppPaths.cs`
  - Verify: dotnet build && dotnet test --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ApiEndpointTests"

- [x] **T02: Add ownership boundary regression checks and slice verification harness** `est:60m`
  ---
estimated_steps: 6
estimated_files: 5
skills_used:
  - test
  - verify-before-complete
---

# T02: Add ownership boundary regression checks and slice verification harness

**Slice:** S01 — Finalize module ownership boundaries
**Milestone:** M001

## Description

Introduce explicit automated checks that fail when runtime primitive ownership drifts back out of Core, then wire `scripts/verify-s01.sh` to enforce build+test boundary invariants.

## Failure Modes

| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| Test discovery/execution (`tests/HappyGymStats.Tests`) | Fix namespace/class naming and include test file in project compile items. | Reduce test scope to ownership-focused cases and keep command deterministic. | Treat unexpected parsing/file enumeration as failure and emit offending path list. |
| Shell verification script (`scripts/verify-s01.sh`) | Fail fast with non-zero exit and clear message for missing command or failed assertion. | Keep script bounded to build/tests + static file checks (no long-running loops). | Validate grep/rg assertions and explicitly error on ambiguous results. |

## Load Profile

- **Shared resources**: test runner process, filesystem traversal of `src/`.
- **Per-operation cost**: one targeted test assembly run + one shell verification script invocation.
- **10x breakpoint**: test duration growth if file-scan assertions become broad; constrain to known duplicate paths/types.

## Negative Tests

- **Malformed inputs**: Simulate reintroduced duplicate file path in assertion logic and verify test/script fails.
- **Error paths**: Ensure verify script exits non-zero when build fails or ownership assertions fail.
- **Boundary conditions**: Empty match set for duplicate-file scan is pass; any single match is fail.

## Steps

1. Add a new ownership-boundary test file under `tests/HappyGymStats.Tests` that scans source paths and asserts duplicate primitive files are absent outside Core.
2. Keep assertions explicit for this slice (`LogFetcher`, `ReconstructionRunner`, `AppPaths`, `Checkpoint` duplicates) to avoid brittle overreach.
3. Update `scripts/verify-s01.sh` to run `dotnet build`, targeted ownership tests, and static assertions (e.g., `test ! -f ...` for removed duplicates).
4. Ensure script output is clear enough for CI and future agents to localize drift quickly.
5. Run script locally and tune failures/messages until deterministic.
6. Confirm existing integration tests still pass alongside new ownership checks.

## Must-Haves

- [ ] Ownership regression test exists and fails if duplicate primitive files/types are reintroduced outside Core.
- [ ] `scripts/verify-s01.sh` enforces slice-level stopping condition in one command.

## Verification

- `dotnet test --filter "FullyQualifiedName~ModuleOwnershipBoundariesTests"`
- `bash scripts/verify-s01.sh`

## Observability Impact

- Signals added/changed: boundary regression produces named failing assertions instead of implicit compile ambiguity.
- How a future agent inspects this: run `bash scripts/verify-s01.sh` for one-shot boundary health.
- Failure state exposed: exact duplicate path/type that violated ownership boundary.

## Inputs

- `scripts/verify-s01.sh` — existing slice verification harness.
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` — test project container.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — style reference for test patterns.
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs` — canonical ownership target.
- `src/HappyGymStats/` — duplicate-path scan scope.

## Expected Output

- `tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs` — boundary regression tests for ownership invariants.
- `scripts/verify-s01.sh` — updated executable slice verification command.
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` — includes new test file if required.
  - Files: `tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `scripts/verify-s01.sh`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `src/HappyGymStats.Core/Fetch/LogFetcher.cs`
  - Verify: dotnet test --filter "FullyQualifiedName~ModuleOwnershipBoundariesTests" && bash scripts/verify-s01.sh

## Files Likely Touched

- src/HappyGymStats/Program.cs
- src/HappyGymStats/Fetch/LogFetcher.cs
- src/HappyGymStats/Reconstruction/ReconstructionRunner.cs
- src/HappyGymStats/Storage/AppPaths.cs
- src/HappyGymStats/Storage/Models/Checkpoint.cs
- src/HappyGymStats.Core/Fetch/LogFetcher.cs
- src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
- src/HappyGymStats.Core/Storage/AppPaths.cs
- tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- scripts/verify-s01.sh
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
