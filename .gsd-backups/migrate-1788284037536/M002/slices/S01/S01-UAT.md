# S01: S01 — UAT

**Milestone:** M002
**Written:** 2026-05-01T20:46:46.473Z

# S01: S01 — UAT

**Milestone:** M002  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: S01 ships planning/research contracts and drift diagnostics (not runtime feature behavior), so proof is correctness/completeness of artifacts and deterministic anchor validation.

## Preconditions

- Repository is on the S01 output state.
- `bash` and `rg` are available.
- Files exist: `endpoint-log-anchor-inventory.md`, `modifier-provenance-taxonomy.md`, and `scripts/verify-s01-taxonomy.sh`.

## Smoke Test

Run `bash scripts/verify-s01-taxonomy.sh` and confirm it ends with `S01 taxonomy drift checks passed.` and exit code 0.

## Test Cases

### 1. Inventory artifact completeness

1. Run: `test -s .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`
2. Run: `rg -n "## API Endpoints|## Torn Fetch Entry|## Extractor Fields|## Known Gaps" .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`
3. **Expected:** File is non-empty and all four sections are present.

### 2. Taxonomy contract completeness

1. Run: `test -s .gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`
2. Run: `rg -n "## Taxonomy Matrix|## Confidence Impact Mapping|## Key Scope Requirements|## Open Unknowns" .gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`
3. **Expected:** File is non-empty and required contract sections exist.

### 3. Anchor drift enforcement

1. Run: `bash scripts/verify-s01-taxonomy.sh`
2. Observe `[PASS]` lines for required sections, API endpoint anchors, taxonomy field candidates, and extractor tokens.
3. **Expected:** Script exits 0 and prints `S01 taxonomy drift checks passed.`

## Edge Cases

### Missing or renamed anchor token

1. Temporarily change one expected token in `LogEventExtractor.cs` (or remove the corresponding taxonomy token).
2. Re-run `bash scripts/verify-s01-taxonomy.sh`.
3. **Expected:** Script exits non-zero with a `[FAIL]` message identifying the missing token/anchor.

## Failure Signals

- Any required heading check fails.
- Missing API endpoint anchor (`/api/v1/torn/gym-trains` or `/api/v1/torn/happy-events`).
- Missing extractor token (`happy_used`, `maximum_happy_before/after`, `happy_increased/decreased`).
- Script non-zero exit from `scripts/verify-s01-taxonomy.sh`.

## Not Proven By This UAT

- Live Torn API payload correctness for faction/company evidence.
- Runtime reconstruction of new provenance records (implemented in later slices).
- Frontend confidence gradient rendering behavior (later slices S04/S05).

## Notes for Tester

Open unknowns for faction/company mappings are expected in S01 and should remain explicitly documented, not silently inferred. This slice is a contract and guardrail foundation for S02/S03 rather than a runtime feature rollout.
