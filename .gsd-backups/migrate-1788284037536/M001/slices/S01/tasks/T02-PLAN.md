---
estimated_steps: 52
estimated_files: 5
skills_used: []
---

# T02: Add ownership boundary regression checks and slice verification harness

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

## Inputs

- `scripts/verify-s01.sh`
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs`
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs`

## Expected Output

- `tests/HappyGymStats.Tests/ModuleOwnershipBoundariesTests.cs`
- `scripts/verify-s01.sh`
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`

## Verification

dotnet test --filter "FullyQualifiedName~ModuleOwnershipBoundariesTests" && bash scripts/verify-s01.sh

## Observability Impact

Verification shifts from ad-hoc manual inspection to explicit failing assertions tied to ownership boundary drift.
