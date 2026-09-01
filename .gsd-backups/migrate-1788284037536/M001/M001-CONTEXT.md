# M001: Core/API Decoupling and DB-Native Pipeline Hardening

**Gathered:** 2026-05-01
**Status:** Ready for planning

## Project Description

Decouple runtime ownership so Core is the single owner of pipeline primitives, then harden the DB-native import/reconstruction pipeline so status survives restarts and derived-data reads remain consistent during failures.

## Why This Milestone

Current architecture still carries boundary ambiguity (Core vs app-local ownership) and operational risk around run-state durability and refresh consistency windows. This milestone exists now to make the API contract trustworthy in real use (including restarts/failures), and to align tests/docs with the implemented DB-native anonymous aggregate model.

## User-Visible Outcome

### When this milestone is complete, the user can:

- Trigger import/reconstruction and still see accurate `/v1/import/latest` and `/v1/import/{id}` status after API restart.
- Read stable derived data during failed refresh attempts because the previous good dataset remains visible until a full successful commit.

### Entry point / environment

- Entry point: `/v1` API endpoints (primary), with CLI parity as supporting path.
- Environment: local dev + production-like API runtime lifecycle (restart/failure behavior).
- Live dependencies involved: database (SQLite), import/reconstruction pipeline, API process lifecycle.

## Completion Class

- Contract complete means: endpoint behavior is provable by DB-native integration tests and status/read artifacts for import→reconstruct→read.
- Integration complete means: Core ownership boundaries, API status endpoints, DB writes, and read surfaces operate together without duplicate primitive drift.
- Operational complete means: restart-safe run-state continuity and failure-safe derived-data visibility hold under real process restarts and mid-refresh failures.

## Final Integrated Acceptance

To call this milestone complete, we must prove:

- A full import→reconstruct→read scenario succeeds via API and matches the DB-native contract.
- After API restart, run-history/status endpoints still return accurate durable timeline data.
- During a failed reconstruction refresh, readers still get the last known good dataset (no partial/empty exposure window).

## Architectural Decisions

### Core as sole runtime primitive owner

**Decision:** Runtime pipeline primitives (`LogFetcher`, `ReconstructionRunner`, `AppPaths`, checkpoint model) are owned only in Core and consumed by API/CLI.

**Rationale:** Eliminates ownership drift and compile/runtime ambiguity, reducing divergence between API and CLI behavior.

**Alternatives Considered:**
- Keep duplicate app-local implementations — rejected due to ongoing drift risk and boundary ambiguity.

---

### Durable run timeline for status APIs

**Decision:** Persist run lifecycle state durably and expose full history through `/v1/import/latest` and `/v1/import/{id}`.

**Rationale:** Mixed internal/external operators need trustworthy post-restart visibility into what ran, failed, or completed.

**Alternatives Considered:**
- Latest-only status snapshot — rejected because it hides operational history needed for diagnosis and confidence.

---

### Last-good dataset read consistency policy

**Decision:** Keep previous derived dataset visible until a full successful refresh commits atomically.

**Rationale:** Prioritizes read consistency and user trust over exposing partial progress.

**Alternatives Considered:**
- Expose partial refresh data — rejected due to inconsistent/ambiguous reads.
- Block all reads during failure windows — rejected as too disruptive for consumers.

---

### Scope boundary: no auth/rate-limit expansion in M001

**Decision:** Keep no-auth aggregate model and defer auth/rate-limit/multi-tenant identity work.

**Rationale:** Protects milestone focus on ownership, durability, consistency, and DB-native contract hardening.

**Alternatives Considered:**
- Include basic auth/rate limiting now — rejected as scope expansion that dilutes core reliability outcomes.

## Error Handling Strategy

Use fail-visible, state-durable pipeline handling: each import/reconstruction run writes explicit lifecycle state transitions (started, progressed, failed, succeeded) into durable storage; API status endpoints read this canonical state. Reconstruction refresh uses transactional swap semantics so failed runs do not publish partial results. Retry behavior is explicit and bounded at orchestration points; failures remain queryable rather than overwritten. User-facing API errors should return clear operation state with stable identifiers (`runId`) and actionable status (failed + reason category), while preserving last-good read behavior.

## Risks and Unknowns

- Ownership regressions reintroduced in later changes — could silently re-fragment runtime behavior without guard tests.
- Transactional refresh edge cases under interruption — may still produce consistency bugs if commit boundaries are wrong.
- Restart lifecycle races around in-flight runs — could misreport status if persistence ordering is incorrect.
- Performance baseline variance with larger synthetic data — may expose reconstruction path bottlenecks after correctness hardening.

## Existing Codebase / Prior Art

- `milestones/M001/M001-ROADMAP.md` — defines milestone vision, success criteria, and slice dependency map (S01–S06).
- `milestones/M001/slices/S01/S01-PLAN.md` — concrete ownership-boundary implementation and verification strategy.
- `src/HappyGymStats.Core/*` — canonical runtime primitive ownership target for fetch/reconstruction/path components.
- `src/HappyGymStats.Api/Program.cs` — API composition root and primary acceptance path entrypoint.
- `tests/HappyGymStats.Tests/*` — integration and boundary-test harness for DB-native contract proof.

## Relevant Requirements

- R001 (implicit, milestone-level) — enforce single runtime ownership boundary in Core.
- R002 (implicit, milestone-level) — durable run-state status continuity across restart.
- R003 (implicit, milestone-level) — atomic derived-data refresh consistency policy.
- R004 (implicit, milestone-level) — DB-native end-to-end test parity for import→reconstruct→read.

## Scope

### In Scope

- Core/API/CLI ownership boundary consolidation for runtime primitives.
- Durable import/reconstruction run state and status endpoint correctness.
- Transactional derived dataset refresh with last-good visibility on failure.
- DB-native end-to-end parity tests and documentation alignment.

### Out of Scope / Non-Goals

- Auth model redesign.
- Rate-limiting/CORS/write-surface hardening.
- Multi-tenant identity/user-scoped data model changes.

## Technical Constraints

- Must preserve API-first acceptance contract while maintaining CLI compatibility.
- Must operate on DB-native architecture (SQLite-backed state/data).
- Must keep derived-data reads consistent during refresh failures.
- Must prove behavior with restart-aware integration evidence, not compile-only checks.

## Integration Points

- API runtime (`/v1/*`) — primary status/read contract surface.
- Core modules — shared primitive ownership and orchestration logic.
- SQLite storage layer — durable run state + derived dataset persistence.
- Test harness (`dotnet test` + slice verify scripts) — enforcement of ownership and DB-native behavior.

## Testing Requirements

Required mix:
- Unit tests for ownership/boundary guards and state transition logic.
- Integration tests for import→reconstruct→read DB-native contract via API.
- Restart-focused tests verifying durable status continuity after process recycle.
- Failure-path tests proving last-good dataset remains readable after failed refresh.
- Slice verification scripts (e.g., S01 boundary guard) must fail fast with actionable output.

## Acceptance Criteria

- API and CLI compile/run using Core-owned primitives only; duplicate ownership paths are removed/guarded.
- `/v1/import/latest` and `/v1/import/{id}` expose accurate durable timeline after restart.
- Derived dataset refresh is atomic from reader perspective; no partial/empty exposure during failed refresh.
- DB-native import→reconstruct→read flow is covered by passing integration tests.
- Docs accurately reflect DB-native anonymous aggregate model and operational constraints.

## Open Questions

- Exact durable run-history retention horizon (unbounded vs capped) — current thinking: timeline is required; retention policy can be set operationally later.
- Final performance baseline threshold for “acceptable” reconstruction time — current thinking: establish benchmark in S04, then lock guardrails based on measured baseline.
- Error payload granularity for failed runs (category schema depth) — current thinking: stable runId + machine-readable failure category + concise human message.