# Weekly audit intake — 2026-09-06

## Provenance and limits

This intake relays a static Claude review performed on 2026-09-06. That run had no GitHub API/write access and no usable .NET SDK/network, so it did not read live issues or PRs, build, test, or run repository verifiers. Current code and the live #140 coordination state remain authoritative.

The raw audit is intentionally not committed because it contains unresolved security-sensitive implementation details. Revalidate every item against current code before mutation and claim the exact scope in #140.

## Immediate remediation carried by this PR

- Prevent request authentication material from being retained in Torn pagination continuations that cross persistence boundaries.
- Add regression coverage for query ordering, key casing, key-only queries, relative URLs, fragments, null/no-query input, and source-level persistence call sites.
- Preserve resume behavior by relying on the existing request path to attach the active credential when following a continuation.

Historical stored-continuation cleanup or credential rotation, if private validation shows either is required, is an operator/security follow-up and is not performed by agents in this PR.

## Sanitized follow-up queue

### Security and privacy

- Revalidate admin transport encryption and make configuration verification enforce the invariant.
- Fail fast on insecure or placeholder provisional-token signing configuration.
- Revalidate tenant/user scoping for cached/public surfaces and faction war-intelligence reads.
- Review authentication, abuse/rate controls, and CORS for anonymous/import interfaces.
- Add response-hardening headers and strengthen log/redaction verifier coverage.
- Recheck provisional identity expiry at the persistence boundary.

### Correctness

- Reconcile SignalR connection lifetime/disposal in the war board service.
- Render viewer-facing timestamps in the browser/viewer timezone rather than server-local time.
- Dispose Torn HTTP responses consistently.
- Revalidate multi-day duration formatting and aggregate war-board labels against current UI work.

### Verification and tooling

- Extend verifier-graph completeness to non-shell verifier assets.
- Reconcile stale/dead workflow branch-proof surfaces with live branch topology.

### Documentation and topology

- Route Markdown remediation through existing canonical #223; do not duplicate it.
- Route branch retirement/topology reconciliation through existing canonical #193; do not delete remote branches autonomously.

## Mixed-patch handling

Claude also proposed UI/auth wiring, duration/label, and CI cleanup hunks. They are intentionally not imported wholesale here because current live work owns overlapping account/Program seams and current repository/PR state must win. Agents should reconcile those findings against live packages before claiming independent work.

## Proof status

Claude's audit performed no build or tests. The focused regression test and GitHub CI for this PR are the first executable proof of the remediation carried here.
