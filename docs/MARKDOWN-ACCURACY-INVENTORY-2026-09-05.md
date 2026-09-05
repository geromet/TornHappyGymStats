# Markdown accuracy inventory — 2026-09-05

This is the canonical audit inventory for inaccurate, stale, contradictory, or materially misleading information in tracked Markdown at `main@f6d686c9706ac2657d9fe30455a8547211993611`.

## Scope and method

The audit enumerated all **33 tracked `*.md` files** on the pinned default head: 3 root files, 3 Claude skill files, 1 PR template, and 26 files under `docs/`. There are no tracked Markdown files under `infra/`, `scripts/`, `src/`, or `tests/` at this snapshot.

Active/current guidance was checked against the pinned repository tree, executable code/contracts, and live issue/PR state where the text makes time-sensitive claims. Dated fleet inventories, append-only activity/prompt archives, and the explicitly baseline-scoped UX research report are treated as historical/research records rather than being called inaccurate merely because the repository later changed. A historical document is a finding only if it presents old state as current authority.

Classification:
- **FINDING** — a concrete inaccurate/stale/contradictory/misleading statement was verified.
- **CURRENT / NO VERIFIED DRIFT** — reviewed without a concrete discrepancy proven in this audit.
- **HISTORICAL / SNAPSHOT** — intentionally dated/provenance material; old state is not itself an error.
- **RESEARCH / GUIDANCE SNAPSHOT** — design/research material explicitly scoped to its baseline/current-issue override.

## Executive summary

- **10 verified findings across 9 files**.
- **3 high**, **6 medium**, **1 low** severity.
- The highest-risk drift is not product prose; it is **agent/workflow authority**: one verifier skill tells agents to edit a router that explicitly says not to do that, and `MILESTONES.md` calls gitignored `workspace/V2/` authoritative despite the canonical clean-clone agreement saying the opposite.
- The human-input queue contains already-resolved P0 work and can send a human toward actions that are no longer required.
- Historical fleet/archive and baseline UX research files are intentionally **not** treated as defects for recording old state.

## Verified findings

| ID | Severity | File | Verified drift | Correct/current evidence | Recommended remediation |
|---|---|---|---|---|---|
| MD-001 | Medium | `README.md` | The audit-context link points to `.gsd/milestones/M003/M003-ROADMAP.md`, but `.gsd/` is not tracked on the pinned head. | Root tree at the pinned SHA contains no `.gsd` directory. | Remove the dead link or replace it with a tracked/current issue/doc reference. |
| MD-002 | Medium | `.claude/skills/looking-at-the-app/SKILL.md` | Quick reference advertises `SHOT_ROUTE=/faction`, but the fake `/faction` page was deliberately deleted. | Merged PR #122 states the route and nav entry were removed and that no `/faction` page remains. | Use an existing stable screenshot route; do not revive the placeholder. |
| MD-003 | **High** | `.claude/skills/writing-a-verify-script/SKILL.md` | The skill first says verifier routing belongs in `manifest.tsv` and to never wire a verifier directly into `build-and-test.sh`, then later instructs the agent to add the script to `build-and-test.sh`. | `scripts/verify/build-and-test.sh` says “ROUTING COMES FROM THE MANIFEST” and “Do not add a verifier call to this file”; `docs/WORKING-AGREEMENT.md` says not to add a second handwritten verifier list. | Delete the direct-router instruction and make manifest registration the single path. |
| MD-004 | **High** | `docs/HUMAN-INPUT-QUEUE.md` | The queue says main has no branch protection and tells the human to wait for #57 / PR #125 before applying required checks. That prerequisite is over. | #56 is closed as completed and records live ruleset `15843258` with required checks; #57 is closed and explicitly completed by merged PR #125. | Remove/archive the completed queue item or rewrite it as resolved history; current human blockers belong in #136 per #140. |
| MD-005 | **High** | `docs/MILESTONES.md` | It calls gitignored `workspace/V2/` “the authoritative hand-off pack” and says to cite those paths only. | `docs/WORKING-AGREEMENT.md` says `workspace/` is supporting material only and never authority; merged PR #121 explicitly removed the old `workspace/V2/` authority model. | Replace with GitHub issues + tracked working agreement as authority; mark old workspace material historical only. |
| MD-006 | Medium | `docs/MILESTONES.md` | Current-reference text is stale in multiple places: it says the standing non-goals live in `CLAUDE.md` (they now live in the working agreement), and M012 says “no issue yet” despite open #90 covering tracked orders/acknowledgements/full war timeline-replay. | `CLAUDE.md` is now only a router; `docs/WORKING-AGREEMENT.md` sections 2–3 carry the boundaries. Issue #90 is open with the timeline/orders contract. | Remove duplicated live-status claims or refresh them to canonical issue references. |
| MD-007 | Low | `docs/SETUP.md` | The frontend/AdminPanel section still frames those projects as potentially living in a separate checkout and offers a “no local source” path, although both source trees are tracked in this repository. | Pinned `src/` contains `HappyGymStats.Blazor` and `HappyGymStats.AdminPanel`. | Make the in-repo paths the normal setup path; keep external-checkout wording only if there is a real supported use case. |
| MD-008 | Medium | `docs/WORKING-AGREEMENT.md` | Evidence guidance still says “Until those checks are merged” for #61/#77. Both checks are already landed. | #61 is closed by merged PR #119; #77 is closed by merged PR #120, and current main mechanically classifies/validates evidence. | Replace the transitional paragraph with the landed behavior and current executable entrypoints. |
| MD-009 | Medium | `docs/architecture/business-logic-and-reconstruction-flow.md` | Its “minimal verification” command posts `{"apiKey":"…","fresh":false}` to `POST /api/v1/torn/import-jobs`; current controller rejects non-fresh anonymous base-endpoint imports and tells callers to resume through authenticated `/me`. | `ImportController.StartImport` requires `request.Fresh == true`; `/api/v1/torn/import-jobs/me` is the authenticated resume/import path. | Fix the command and update the “current flow” section to distinguish fresh anonymous/base import from authenticated `/me`. |
| MD-010 | Medium | `docs/UX-PLAN.md` | The static implementation table is materially stale even though the file says issues are authoritative: it says #105 is “not started” although #105 is completed, #106 is only “partly done” although it is closed/absorbed into #95 after a completed slice, and #98 is “not started” although live #98 is explicitly PARTIAL with its server lifecycle landed. | Live #105, #106, and #98 bodies/states on 2026-09-05. | Remove the duplicate status column or regenerate it from issues; keep the file as a stable UX principles/pointer document. |

## Important non-findings / false positives rejected

- `docs/M004-MY-STATS-OPERATOR-GATE.md` looked old, but the pinned code still has `/my-stats`, authenticated `GET /api/v1/torn/surfaces/me`, authenticated `POST /api/v1/torn/import-jobs/me`, and the documented identity-map failure boundary. It is **not** listed as stale merely because newer Account & Connections work exists.
- `docs/fleet/BRANCH-INVENTORY-2026-09-05.md` records old branch state by design. #140 explicitly describes it as historical recovery provenance, not live existence authority.
- `docs/fleet/archive/activity/2026-09.md` and `docs/fleet/archive/instruction-changes.md` are append-only historical records; changing current repo state does not invalidate their old snapshots.
- `docs/ux/*` explicitly states its research baseline and that current repository/issues supersede baseline observations. Its old observations are therefore not automatically current-state defects.
- Torn API reference/sample documents were not marked wrong without a concrete contract/code discrepancy.

## Full 33-file coverage ledger

| File | Classification | Notes |
|---|---|---|
| `AGENTS.md` | CURRENT / NO VERIFIED DRIFT | Correct clean-clone router to working agreement/issues/LOCK. |
| `CLAUDE.md` | CURRENT / NO VERIFIED DRIFT | Correctly reduced to Claude-specific router. |
| `README.md` | **FINDING** | MD-001. |
| `.claude/skills/fixing-a-bug/SKILL.md` | CURRENT / NO VERIFIED DRIFT | No concrete repo-state contradiction proven. |
| `.claude/skills/looking-at-the-app/SKILL.md` | **FINDING** | MD-002. |
| `.claude/skills/writing-a-verify-script/SKILL.md` | **FINDING** | MD-003. |
| `.github/pull_request_template.md` | CURRENT / NO VERIFIED DRIFT | Matches structured evidence contract. |
| `docs/DEPLOYMENT.md` | CURRENT / NO VERIFIED DRIFT | No concrete discrepancy proven in this audit. |
| `docs/HUMAN-INPUT-QUEUE.md` | **FINDING** | MD-004. |
| `docs/M004-MY-STATS-OPERATOR-GATE.md` | CURRENT / NO VERIFIED DRIFT | Key route/API/identity contracts still exist on pinned code. |
| `docs/MILESTONES.md` | **FINDING** | MD-005, MD-006. |
| `docs/OPERATIONS-PITFALLS.md` | HISTORICAL / SNAPSHOT | Past incidents/fixes are clearly narrated as operational history. |
| `docs/OVERVIEW.md` | CURRENT / NO VERIFIED DRIFT | No concrete discrepancy proven. |
| `docs/SETUP.md` | **FINDING** | MD-007. |
| `docs/UX-PLAN.md` | **FINDING** | MD-010. |
| `docs/WORKING-AGREEMENT.md` | **FINDING** | MD-008; otherwise remains canonical tracked agreement. |
| `docs/architecture/business-logic-and-reconstruction-flow.md` | **FINDING** | MD-009. |
| `docs/fleet/BRANCH-INVENTORY-2026-09-05.md` | HISTORICAL / SNAPSHOT | Dated recovery provenance, explicitly not live authority. |
| `docs/fleet/SELF-IMPROVING-FLEET.md` | CURRENT / NO VERIFIED DRIFT | No concrete contradiction proven. |
| `docs/fleet/archive/activity/2026-09.md` | HISTORICAL / SNAPSHOT | Append-only activity archive. |
| `docs/fleet/archive/instruction-changes.md` | HISTORICAL / SNAPSHOT | Append-only instruction history. |
| `docs/torn-api/endpoints-and-log-types.md` | CURRENT / NO VERIFIED DRIFT | No concrete implementation discrepancy proven. |
| `docs/torn-api/faction-members-contract.md` | CURRENT / NO VERIFIED DRIFT | Recent pinned OpenAPI contract for #86. |
| `docs/torn-api/helper-curl-log-samples.md` | CURRENT / NO VERIFIED DRIFT | No concrete contract discrepancy proven. |
| `docs/torn-api/terms-of-service.md` | CURRENT / NO VERIFIED DRIFT | Recent versioned disclosure; historical changelog references remain history. |
| `docs/ux/README.md` | RESEARCH / GUIDANCE SNAPSHOT | Explicit baseline + current-state override. |
| `docs/ux/00-executive-and-design-principles.md` | RESEARCH / GUIDANCE SNAPSHOT | Baseline/design guidance. |
| `docs/ux/01-current-audit-and-competitive-context.md` | RESEARCH / GUIDANCE SNAPSHOT | “Current” is scoped by report baseline/publication note. |
| `docs/ux/02-tactical-calm-and-application-shell.md` | RESEARCH / GUIDANCE SNAPSHOT | Design guidance. |
| `docs/ux/03-page-blueprints-content-and-interaction.md` | RESEARCH / GUIDANCE SNAPSHOT | Design guidance. |
| `docs/ux/04-performance-accessibility-and-design-system.md` | RESEARCH / GUIDANCE SNAPSHOT | Design guidance. |
| `docs/ux/05-verification-roadmap-and-acceptance.md` | RESEARCH / GUIDANCE SNAPSHOT | Roadmap/research-time notes are explicitly contextual. |
| `docs/ux/06-limitations-and-sources.md` | RESEARCH / GUIDANCE SNAPSHOT | Explicit limitations/baseline sources. |

## Remediation order

1. **Agent/workflow authority first:** MD-003, MD-005, MD-004, MD-008.
2. **Commands/routes that lead directly to failure:** MD-002, MD-009, MD-001.
3. **Duplicate status/topology drift:** MD-006, MD-010, MD-007.

## Done criteria for the remediation tracker

- Every FINDING above is either corrected, deliberately archived with unmistakable historical labeling, or disproven with current evidence and removed from the inventory.
- Agent-facing guidance has one authority path: tracked working agreement + executable verifier contracts + GitHub issues/LOCK; no `workspace/V2` authority language remains.
- All documented commands/routes used as current examples resolve against then-current code.
- Time-sensitive status tables/queues are removed, generated, or explicitly snapshot-dated so they cannot masquerade as live issue state.
- A fresh Markdown enumeration on the then-current default head records the new file count and confirms no new current-state contradiction was introduced.

This audit is inventory-only. It does not remediate product/runtime behavior and must not be interpreted as authority to merge into `main` outside Gerome/coding-agent review.