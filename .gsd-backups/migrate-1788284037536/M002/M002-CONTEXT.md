# M002: Modifier Provenance & Accuracy Gradient

**Gathered:** 2026-05-01
**Status:** Ready for planning

## Project Description

Add provenance-aware modifier reconstruction so Torn gym surface points carry explicit evidence quality, expose that quality in API payloads, and visualize it as a red→green confidence gradient in the dashboard.

## Why This Milestone

Current reconstruction can produce values without clearly communicating whether personal/faction/company modifier evidence is complete. This milestone closes that trust gap now so downstream planning/execution can distinguish verified vs partial intervals, prioritize missing-data recovery, and avoid presenting uncertain points as equally reliable.

## User-Visible Outcome

### When this milestone is complete, the user can:

- See each surface point colored by confidence (red→green) with tooltip reasons tied to evidence coverage.
- Identify exactly which owner/faction/company evidence is missing and act on actionable guidance.

### Entry point / environment

- Entry point: `/api/v1/torn/surfaces/latest` + static dashboard in `web/`
- Environment: local dev and production-like hosted static frontend + separately hosted API
- Live dependencies involved: Torn API/log payloads, SQLite, reconstruction pipeline

## Completion Class

- Contract complete means: confidence fields/reason codes are present and schema-tested; provenance states are represented for personal/faction/company intervals.
- Integration complete means: import/reconstruction, DB model, API serializer, and frontend rendering all agree on confidence semantics and reason codes.
- Operational complete means: missing-data warnings are actionable for operator workflow and remain stable across incremental imports.

## Final Integrated Acceptance

To call this milestone complete, we must prove:

- A real dataset import produces mixed-confidence points where verified evidence renders green and partial evidence renders red/orange with correct reason metadata.
- Unresolved owner/faction/company dependencies surface explicit warnings that identify what is missing and where to recover it.
- Confidence cannot be treated as done from synthetic-only checks; at least one run must use real Torn-derived logs because provenance gaps are the core behavior.

## Architecture Delta (Before → After)

- **Runtime ownership**
  - Before: fetch/reconstruction/storage responsibilities were split across API/CLI-era boundaries.
  - After: shared runtime orchestration is centralized in `HappyGymStats.Core`.

- **Dependency direction**
  - Before: project references allowed layer leakage and mixed concerns.
  - After: `HappyGymStats.Api` and `HappyGymStats.Cli` depend on `HappyGymStats.Core`; Core depends on `HappyGymStats.Data` and `HappyGymStats.Visualizer`.

- **Legacy export boundary**
  - Before: CSV/export and migration utilities lived in CLI paths, mixed with active runtime concerns.
  - After: legacy export/migration tooling is isolated under `HappyGymStats.Legacy` to keep modern runtime layers clean.

- **Surface dataset behavior**
  - Before: pointcloud behavior was constrained to recent-run-style visibility in practice.
  - After: surfaces payload/render path supports full available dataset display from cached series arrays.

## Architectural Decisions

### Operator-first confidence workflow

**Decision:** Optimize M002 primarily for operator remediation workflow (not passive viewer-only display).

**Rationale:** User selected operator-first, so the milestone must prioritize actionable diagnostics and recovery guidance when confidence is low.

**Alternatives Considered:**
- Dashboard-user-first — not chosen because it under-prioritizes the workflow that actually resolves low-confidence data.

### Deterministic confidence contract

**Decision:** Use deterministic rule-based confidence with explicit reason codes for this milestone.

**Rationale:** User selected deterministic rules, which gives auditable, testable behavior and stable semantics across slices.

**Alternatives Considered:**
- Hybrid weighted score — deferred to later evolution; adds tuning ambiguity before provenance taxonomy is fully proven.

### Manual override policy for M002

**Decision:** Keep faction/company overrides read-only in M002; provide warnings and guidance but no manual mutation path yet.

**Rationale:** User explicitly chose read-only warnings for this milestone scope.

**Alternatives Considered:**
- Allow overrides with audit markers — not chosen for M002 to avoid introducing write-path governance before provenance baseline is stable.

---

> Add additional decisions as separate `### Decision Title` blocks following the same structure above.
> See `.gsd/DECISIONS.md` for the full append-only register of all project decisions.

## Error Handling Strategy

Reconstruction should propagate provenance uncertainty as structured non-fatal states (reason codes + warnings) rather than hard-fail point generation. Import/API failures remain explicit errors with actionable messages; missing dependency evidence is treated as degradable confidence with retry-on-next-import behavior. For external fetch/transient issues, keep existing retry policy and preserve last known evidence state; do not silently upgrade confidence without matching proof.

## Risks and Unknowns

- Torn endpoint/log taxonomy drift — missing or changed fields can invalidate confidence mapping rules.
- Owner/faction/company linkage completeness — unresolved identity mapping may keep large ranges low-confidence.
- Confidence UX interpretation — gradient/tooltip wording may still be misunderstood without careful reason text.

## Existing Codebase / Prior Art

- `milestones/M002/M002-ROADMAP.md` — defines slice plan and required outcomes for provenance + confidence flow.
- `src/HappyGymStats.Core` reconstruction pipeline — base path where modifier interval verification must be extended.
- `src/HappyGymStats.Data` SQLite/EF Core layer — target for provenance/state persistence.
- `src/HappyGymStats.Api` (`/api/v1/torn/*`) — payload surface that must expose confidence + reason metadata.
- `web/` static frontend — point-cloud gradient and tooltip explanation surface.

## Relevant Requirements

- R001 (existing data pipeline reliability scope) — advanced by adding explicit verified/unverified modifier provenance rather than implicit trust.
- R002 (API/frontend consistency scope) — advanced by shared confidence semantics and reason-code contract across backend and UI.

## Scope

### In Scope

- Provenance-aware modifier interval modeling for personal/faction/company dimensions.
- Deterministic confidence scoring + reason metadata in API response.
- Frontend red→green confidence visualization with legend/tooltips.
- Actionable missing-data warnings for operator remediation.

### Out of Scope / Non-Goals

- Probabilistic/ML confidence models.
- Enabling manual faction/company override writes in this milestone.
- Broad UI redesign unrelated to confidence/provenance interpretation.

## Technical Constraints

- Must remain compatible with static frontend + separate API deployment boundary (D001).
- Must preserve SQLite-first operational model and existing import cadence.
- Confidence semantics must be deterministic and testable from artifact evidence.

## Integration Points

- Torn logs/endpoints — source of modifier evidence and dependency gaps.
- Reconstruction engine — computes interval verification and unresolved dependencies.
- SQLite/EF Core — stores provenance state and confidence inputs.
- API serializer — surfaces confidence score and reason metadata contract.
- Frontend visualization — maps confidence to gradient and explanatory UX.

## Testing Requirements

Unit tests for confidence rule evaluation and reason-code generation; integration tests for import→reconstruct→DB→API flow with mixed completeness fixtures; UI tests for gradient rendering and tooltip explanation consistency; end-to-end proof with at least one real Torn-derived dataset showing both high and low confidence paths.

> Specify test types (unit, integration, e2e), coverage expectations, and specific test scenarios that must pass.

## Acceptance Criteria

- API includes per-point confidence + deterministic reason metadata for all returned points.
- Provenance state distinguishes verified vs unverified intervals across personal/faction/company.
- Frontend renders red→green confidence gradient with clear legend and reason tooltips.
- Missing owner/faction/company evidence yields actionable warnings aligned to operator-first workflow.
- Manual override remains read-only (warnings/guidance only) for M002 scope.

> Per-slice acceptance criteria gathered during discussion. Each slice should have clear, testable criteria.

## Open Questions

- Exact reason-code taxonomy granularity — current thinking: start coarse (missing-owner, missing-faction, missing-company, stale-evidence) then refine in S01/S04 if needed.
- Warning action affordances (links/details) in UI — current thinking: provide links where resolvable identity data exists; otherwise show explicit unresolved identifiers.