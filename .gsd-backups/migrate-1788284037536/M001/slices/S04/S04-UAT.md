# S04: S04 — UAT

**Milestone:** M001
**Written:** 2026-04-30T23:24:45.223Z

# S04: S04 — UAT

**Milestone:** M001  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: S04’s deliverable is a reproducible benchmark + baseline artifact contract; correctness is proven by deterministic benchmark execution and artifact integrity checks rather than interactive runtime UX.

## Preconditions

- .NET 8 SDK is installed and available.
- Repository dependencies restore successfully.
- Benchmark verification script is executable: `scripts/verify-s04-benchmark.sh`.

## Smoke Test

Run `bash scripts/verify-s04-benchmark.sh` once and confirm it exits 0 while printing the benchmark artifact path.

## Test Cases

### 1. Benchmark pipeline executes end-to-end on synthetic large dataset

1. Execute: `bash scripts/verify-s04-benchmark.sh`.
2. Observe script output includes the benchmark test run (`ReconstructionPerformanceBenchmarkTests`).
3. **Expected:** Test run reports `Passed` for the benchmark test and script exits successfully.

### 2. Machine-readable artifact contract is enforced

1. After script execution, inspect path reported by the script: `tests/HappyGymStats.Tests/bin/Debug/net8.0/BenchmarkArtifacts/reconstruction-benchmark.json`.
2. Confirm the file exists and is non-empty.
3. Confirm JSON includes `durationMs`.
4. **Expected:** All checks pass; script would fail immediately if artifact is missing/empty or if `durationMs` is absent.

### 3. Baseline documentation matches executable verification path

1. Open `docs/performance/reconstruction-baseline.md`.
2. Verify it includes reproducibility details: command, dataset shape/size assumptions, environment assumptions, and baseline interpretation.
3. Verify documented command aligns with current verification workflow (`bash scripts/verify-s04-benchmark.sh`).
4. **Expected:** Documentation provides sufficient detail for a future agent/operator to reproduce and compare benchmark output.

## Edge Cases

### Artifact contract regression (missing metric)

1. Simulate or reason about a benchmark output change where `durationMs` is no longer emitted.
2. Run `bash scripts/verify-s04-benchmark.sh`.
3. **Expected:** Script fails with a clear artifact-validation error, preventing silent baseline drift.

## Failure Signals

- Benchmark test fails or does not execute.
- Verification script exits non-zero.
- Benchmark artifact file is missing, empty, or missing required timing field (`durationMs`).
- Baseline documentation cannot be used to reproduce the benchmark command/environment.

## Not Proven By This UAT

- Absolute performance on all hardware classes or CI runners.
- Longitudinal trend stability across multiple commits/time windows.
- Runtime production latency under live API load (this slice benchmarks reconstruction path in controlled test context).

## Notes for Tester

This UAT is intentionally contract-focused: S04 is about establishing a trustworthy baseline and reproducible measurement path. Use the script output artifact as the source of truth for downstream comparisons in optimization and parity slices.
