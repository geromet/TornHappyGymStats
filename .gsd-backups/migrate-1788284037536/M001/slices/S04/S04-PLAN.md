# S04: Incremental reconstruction performance baseline

**Goal:** Establish a repeatable, DB-native reconstruction performance baseline on large synthetic input so future optimization work has bounded, comparable timing evidence.
**Demo:** Large synthetic dataset benchmark shows bounded reconstruction time and documented baseline.

## Must-Haves

- 1) A benchmark harness can generate or load a large synthetic dataset and run reconstruction end-to-end against SQLite in a deterministic way.
- 2) Benchmark output captures at least total duration plus key throughput counters (events processed, derived rows written) and writes machine-readable results for trend comparison.
- 3) A documented baseline exists in the repo with exact command(s), dataset size, environment assumptions, and current measured bounds.
- 4) Verification proves the benchmark executes successfully and produces non-empty baseline artifacts.

## Proof Level

- This slice proves: operational

## Integration Closure

Consumes S03 transactional reconstruction behavior as the correctness baseline and adds no new runtime entrypoint wiring; it closes the observability/performance-proof gap before S05 extends DB-native parity coverage.

## Verification

- Adds explicit reconstruction timing/counter artifacts so regressions are diagnosable from committed benchmark outputs and reproducible commands.

## Tasks

- [x] **T01: Add deterministic reconstruction benchmark harness and synthetic fixture generator** `est:1.5h`
  Create a repeatable benchmark path that exercises the real reconstruction pipeline against SQLite with a large synthetic log dataset. This closes the biggest execution risk first: without a deterministic harness, later baseline numbers are not trustworthy or comparable across runs.

Use existing Core runtime entrypoints (ReconstructionRunner and AppPaths) rather than introducing test-only reconstruction logic so measurements represent production code paths.
  - Files: `tests/HappyGymStats.Tests/Performance/ReconstructionPerformanceBenchmarkTests.cs`, `tests/HappyGymStats.Tests/TestUtilities/SyntheticLogFixtureBuilder.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ReconstructionPerformanceBenchmarkTests"

- [x] **T02: Publish baseline report and executable verification command** `est:45m`
  Run the new benchmark flow, capture current baseline bounds, and document them in a tracked report with reproducibility details (dataset shape, machine/runtime assumptions, command line, and result interpretation). This task turns raw timing into an operational contract the next slices can reference.

Keep the baseline documentation concise but machine-checkable: include explicit artifact path and a command that fails when the artifact is missing/empty.
  - Files: `docs/performance/reconstruction-baseline.md`, `tests/HappyGymStats.Tests/Performance/ReconstructionPerformanceBenchmarkTests.cs`, `scripts/verify-s04-benchmark.sh`
  - Verify: bash scripts/verify-s04-benchmark.sh

## Files Likely Touched

- tests/HappyGymStats.Tests/Performance/ReconstructionPerformanceBenchmarkTests.cs
- tests/HappyGymStats.Tests/TestUtilities/SyntheticLogFixtureBuilder.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs
- docs/performance/reconstruction-baseline.md
- scripts/verify-s04-benchmark.sh
