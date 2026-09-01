---
id: S08
parent: M003
milestone: M003
provides:
  - Current-state documentation contract for README, overview, setup, deployment, and API examples.
  - Deterministic S08 docs drift verifier for future agents and CI/local checks.
  - Clarified operational peer model for Blazor/AdminPanel plus legacy static frontend labeling.
requires:
  - slice: S01
    provides: API production runtime contract, health route, env requirements, and loopback/nginx verification semantics.
  - slice: S02
    provides: Blazor-to-API loopback boundary and frontend failure taxonomy.
  - slice: S03
    provides: AdminPanel setup/service prerequisites and loopback health contract.
  - slice: S04
    provides: AdminPanel nginx exposure, public health, and protected route expectations.
  - slice: S05
    provides: Canonical production smoke command and full-stack verification taxonomy.
  - slice: S06
    provides: Shared deployment configuration, setup/deploy split, and deploy precondition marker conventions.
  - slice: S07
    provides: Postgres provider test tier and setup-doc context for production-provider parity.
affects:
  - S09
  - future docs/planning agents
  - operators using README/docs/.http examples
key_files:
  - README.md
  - docs/OVERVIEW.md
  - docs/SETUP.md
  - docs/DEPLOYMENT.md
  - src/HappyGymStats.Api/HappyGymStats.Api.http
  - scripts/verify/s08-docs-contract.sh
  - .gsd/PROJECT.md
key_decisions:
  - Treat `web/` static frontend as legacy/historical and Blazor as the primary operational frontend in documentation.
  - Document Blazor and AdminPanel as operational peers when their source projects are absent from this checkout, anchoring behavior to service/deploy/smoke contracts.
  - Use the M003 roadmap/audit context as the in-repo audit reference because the originally referenced docs audit file is absent.
  - Use Minimal API route definitions rather than non-existent Controllers as the authority for API `.http` examples.
patterns_established:
  - Documentation contract drift verifier with file existence checks, required marker checks, stale-claim absence guards, and a stable success sentinel.
  - Docs distinguish repository-owned code from deployed operational peers to avoid inventing missing local paths.
  - Runnable-ish `.http` examples use safe placeholder variables and current `/api/v1/torn/...` routes.
observability_surfaces:
  - `scripts/verify/s08-docs-contract.sh` diagnostic command prints PASS/FAIL checks and `S08 docs contract drift checks passed.` on success.
  - Docs now point operators to `scripts/verify/production-smoke.sh`, API health, AdminPanel health, and deployment precondition markers for runtime diagnosis.
drill_down_paths:
  - .gsd/milestones/M003/slices/S08/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S08/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S08/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S08/tasks/T04-SUMMARY.md
  - .gsd/milestones/M003/slices/S08/tasks/T05-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-07T20:09:33.170Z
blocker_discovered: false
---

# S08: Refactor docs and API examples into current-state contract

**README, operational docs, API `.http` examples, and a deterministic docs drift verifier now describe the current Blazor + API + Postgres + Keycloak + AdminPanel deployment contract.**

## What Happened

S08 converted stale documentation into a current-state operational contract for future agents and operators. `README.md` now identifies Blazor as the primary frontend, Postgres/Keycloak/AdminPanel as part of the production shape, `web/` as legacy/historical, and the production smoke/audit context as the verification entrypoint. `docs/OVERVIEW.md` now separates repository-owned projects from operational runtime peers, documents the import→reconstruct→surfaces data flow, and records loopback boundaries for API, Blazor, and AdminPanel. `docs/SETUP.md` and `docs/DEPLOYMENT.md` now list required env var names, local API run guidance, setup-vs-deploy split, current systemd service names, nginx route expectations, AdminPanel bootstrap flow, smoke-script usage, and secret-handling constraints without embedding secret values. `src/HappyGymStats.Api/HappyGymStats.Api.http` was rewritten from stale `/v1/*` examples to the live Minimal API `/api/v1/torn/...` route contract for health, surfaces, and import jobs using safe placeholders. Finally, `scripts/verify/s08-docs-contract.sh` was added as the slice's deterministic drift gate, checking current-state tokens and rejecting stale static-dashboard, SQLite-primary, and non-v1 route claims.

## Verification

Fresh slice-level verification was run after the final `.gsd/PROJECT.md` refresh: `bash scripts/verify/s08-docs-contract.sh` via `gsd_exec` exited 0 in 43ms and printed the success sentinel `S08 docs contract drift checks passed.` Task-level evidence also passed: README current-state/stale-phrase grep gate exited 0; overview current architecture/loopback/stale-phrase gate exited 0; setup/deployment env/service/smoke marker gate exited 0; API `.http` `/api/v1/torn/...` marker and stale `/v1` absence gate exited 0; and the drift verifier itself exited 0. The verification is documentation-contract proof: it mechanically verifies file presence, required route/service/env/smoke markers, and absence of known stale claims, while leaving prose clarity and live production behavior to human review and S05/S09 runtime gates.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

The referenced audit report file `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md` was absent, so docs link/summarize the M003 roadmap/audit context while preserving the audit timestamp token. The plan referenced Blazor/AdminPanel source paths and API Controllers that do not exist in this checkout; S08 documented Blazor/AdminPanel as operational peers and used Minimal API `Program.cs` routes as the authority for `.http` examples. `.gsd/PROJECT.md` was also refreshed as required by auto-mode.

## Known Limitations

This slice proves documentation contract drift mechanically, not live production behavior. Human prose review may still improve readability. S09 still needs to document/verify runtime, SDK, and package restore reproducibility assumptions. The originally referenced audit report remains absent from `docs/`.

## Follow-ups

S09 should incorporate the new S08 documentation contract and add runtime/package reproducibility details. If the missing audit report is later restored, update docs to link to that file directly instead of only the M003 roadmap/audit context.

## Files Created/Modified

- `README.md` — Updated current-state summary, architecture entrypoints, legacy static frontend labeling, and verification/audit links.
- `docs/OVERVIEW.md` — Rewrote architecture overview around current repo projects, operational peers, data flow, API routes, and loopback boundaries.
- `docs/SETUP.md` — Updated local setup and env contract guidance for API, Postgres, Keycloak, surfaces cache, and verification commands.
- `docs/DEPLOYMENT.md` — Updated deploy roots, service names, setup-vs-deploy split, nginx routes, AdminPanel bootstrap, and smoke usage.
- `src/HappyGymStats.Api/HappyGymStats.Api.http` — Replaced stale `/v1/*` examples with current `/api/v1/torn/...` health, surfaces, and import-job examples.
- `scripts/verify/s08-docs-contract.sh` — Added deterministic docs drift verifier for current-state markers and stale-claim rejection.
- `.gsd/PROJECT.md` — Refreshed project status to record S08 as latest completed slice and document new docs/API-example verification state.
