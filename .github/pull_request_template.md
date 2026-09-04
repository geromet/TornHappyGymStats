## Summary

<!-- What changed and why? Keep this short. -->

## Evidence

<!--
Keep this block machine-readable. #61 computes required tiers plus whether the
diff crosses a security boundary; the PR evidence check verifies both.

Tier meanings:
- T1: source/contracts/build/tests
- T2: rendered UI/browser/screenshot proof
- T3: deploy/remote/operator-boundary proof
- T4: real PostgreSQL/relational proof

If required proof has not run yet, put that tier in `unverified`. If the
classifier reports a security-boundary change, `security-negative-control`
must name the forbidden path and the observed rejection/failure.
-->
<!-- hgs-evidence
task: #ISSUE
lease: none
required: T1
observed: T1
unverified: none
regression: describe the check/negative control that would fail without this change
security-negative-control: n/a
tier2: n/a
tier3: n/a
tier4: n/a
-->
