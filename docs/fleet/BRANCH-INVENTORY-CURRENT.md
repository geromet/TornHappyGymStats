# Current Remote Branch Frontier

Live companion to `docs/fleet/BRANCH-INVENTORY-2026-09-05.md` after the 2026-09-05 recovery and branch-retirement pass. The dated inventory remains the historical recovery ledger; **do not use its rows as proof that a remote ref still exists**.

Snapshot baseline: `main@f6d686c9706ac2657d9fe30455a8547211993611`, refreshed 2026-09-05 20:18 Europe/Amsterdam.

## Retirement reconciliation

The dedicated #193 retirement workflow reports **39 of 41 candidate branches retired**. Its execution log records **40 remote deletions total** during the interval; the remote collection moved from 66 to 27 because a new #220 branch was also created during that period. After the archive rollup branch for #221 was created, the refreshed GitHub branch collection contains **28 remote refs total, including `main`**.

These numbers describe different accounting sets and are not contradictory. #193 remains the source for destructive-retirement decisions and restore-ledger details. This file is read-only inventory; fleet/AUDIT must not delete refs from it.

## Default-destined live review surfaces

| Remote branch | Review surface | Current disposition |
| --- | --- | --- |
| `agent/issue-101-home-gym-explorer` | #200 → `main` | **OPEN / ACTIVE**. Head `4403e124c86fc083a175f2349d0ae4763f663e0b`; forward reconciliation is separately owned. Preserve. |
| `agent/issue-217-security-no-prerender` | #220 → `main` | **OPEN / ACTIVE PROOF**. Head `5df8d04fdf7a659a8966b2ceba5ef7b47826b5bd`; #217 T2/exact-head review is separately owned. Preserve. |
| `fleet/stable/fleet-archive-20260905-1952` | #221 → `main` | **OPEN / GEROME REVIEW**. Head `bd2be0f7cd7d83e3defddea352ab1fb3b756ac6e`; durable archive rollup. Preserve. |

No other currently enumerated remote ref is evidence of stranded work merely because it exists or is ahead of `main`.

## Explicit preservation exception

- `fix/align-prod-nginx-conf-name` — **PRESERVE / LOCAL-WORKTREE EXCEPTION** under #193. The retirement workflow observed a live local worktree/session and intentionally refused remote deletion. Its feature content previously landed through #166; existence of this ref is not new backlog.

## Current remote ref collection

The GitHub branch API returned the following 28 refs at this snapshot. This is an **existence list, not a work queue**. For historical status, landed/superseded ancestry, and recovery provenance, consult the dated inventory and PR history; for destructive cleanup consult #193.

```text
agent/issue-100-chain-planner
agent/issue-101-home-gym-explorer
agent/issue-217-security-no-prerender
docs/agent-terminal-handoff-20260905
feat/p0-95-shared-state-components
feat/p0-95-theme-centralization
feat/shared-member-state-adoption
feat/war-objective-persistence-auth
feat/war-scout-provenance
fix/align-prod-nginx-conf-name
fix/p0-95-remove-fake-theme-selector
fix/p1-103-home-import-error-member-safe
fix/p1-103-login-member-safe
fix/p1-103-mystats-import-error-member-safe
fix/p1-103-player-account-member-safe
fleet/b/72-warpoller-timeprovider
fleet/b/89-objective-consumption
fleet/b-86-opponent-pressure
fleet/b-86-readiness-core
fleet/b-86-travel-availability
fleet/docs-self-improvement-archive
fleet/stable/fleet-archive-20260905-1952
fleet/stable/ux-proof-provenance
fleet/stable/war-core-simplification
fleet/stable/war-objectives
main
proof/shared-state-rendered-t2
refactor/gym-trains-pass-through
```

## Interpretation rules

1. `ahead` or commit-identity divergence after squash/stable/rollup merges is **not** proof of missing work.
2. A useful pushed branch must be represented directly or transitively by a current default-destined PR, proven incorporated/superseded with that relationship recorded, or explicitly abandoned after unique-work assessment.
3. The three live review branches above satisfy terminal visibility through open PRs #200/#220/#221.
4. Historical surviving refs must not be re-queued solely because they remain remote. Reconcile against current `main`, the dated inventory, merged/closed PR history, #218 and current LOCK claims first.
5. Remote deletion remains exclusively the #193 dedicated cleanup/coding-agent workflow after local-worktree, unpushed-work, freshness, open-PR/issue and expected-head checks.
6. If branch retirement or new review branches change the remote collection again, refresh this companion inventory and #193/#218 before treating branch names as discovery input.

## Related control plane

- #140 — live ownership/capacity coordination.
- #218 — open-issue completion/consolidation map.
- #193 — branch-retirement safety contract and execution ledger.
- `docs/fleet/BRANCH-INVENTORY-2026-09-05.md` — historical recovery map, not live existence authority.

No branch was deleted and no default branch was mutated by this inventory update.