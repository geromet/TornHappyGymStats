---
phase: 12
phase_name: Structured Learnings Extraction
project: M001
generated: 2026-04-30T23:50:00Z
counts:
  decisions: 4
  lessons: 4
  patterns: 4
  surprises: 2
missing_artifacts: []
---

### Decisions
- Chose Core as the sole owner of runtime primitives (LogFetcher, ReconstructionRunner, AppPaths, Checkpoint) and removed CLI duplicates to eliminate boundary drift.
  Source: S01-SUMMARY.md/key_decisions
- Chose ImportRuns-backed durable lifecycle state as the canonical source for import status endpoints, including restart-safe latest/by-id retrieval.
  Source: S02-SUMMARY.md/key_decisions
- Chose transactional clear+insert for derived tables so failed reconstruction attempts roll back and preserve last-good reads.
  Source: S03-SUMMARY.md/key_decisions
- Chose DB-native parity definition (durable DB state + endpoint coherence) over legacy CLI export comparison semantics.
  Source: S05-SUMMARY.md/key_decisions

### Lessons
- Testing restart safety is more reliable when assertions target stable run identity and valid lifecycle progression instead of a single fixed transient status.
  Source: S02-SUMMARY.md/What Happened
- API no-empty-window contract tests are less brittle when they compare pre/post response identity rather than fixed cardinalities.
  Source: S03-SUMMARY.md/Deviations
- Benchmark evidence is operationally trustworthy only when artifact integrity is machine-checked (exists, non-empty, required fields like `durationMs`).
  Source: S04-SUMMARY.md/Verification
- Documentation drift is reduced when README semantic claims are paired with executable `.http` examples and marker-based checks.
  Source: S06-SUMMARY.md/patterns_established

### Patterns
- Boundary-consolidation work should pair implementation removal with explicit regression guards (targeted ownership tests + static absence checks + one verify script).
  Source: S01-SUMMARY.md/patterns_established
- Persist lifecycle transitions at each import state change and serve status reads through DB-backed query methods for restart-safe observability.
  Source: S02-SUMMARY.md/patterns_established
- Use transactional dataset swap with deterministic failure injection seams to prove rollback and consumer consistency guarantees.
  Source: S03-SUMMARY.md/patterns_established
- Use deterministic synthetic fixtures plus artifact-first scripted verification as the standard performance-baseline pattern.
  Source: S04-SUMMARY.md/patterns_established

### Surprises
- Hosted test timing advanced in-memory lifecycle state between reads, requiring lifecycle-aware endpoint assertions instead of strict latest-status equality assumptions.
  Source: S05-SUMMARY.md/Deviations
- The docs slice revealed meaningful contract drift risk even after passing technical tests, confirming that documentation needs explicit executable drift gates.
  Source: S06-SUMMARY.md/What Happened