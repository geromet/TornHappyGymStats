# Remote Branch Inventory and Recovery Map — 2026-09-05

This document is the coding-agent handoff for the remote branch audit and stranded-work recovery performed on 2026-09-05.

## Audit basis

- Repository: `geromet/TornHappyGymStats`
- Default branch: `main`
- Default head throughout this recovery: `270666a030e0473c2891fef9e0fd696a6c0df443`
- Original remote inventory: **62 branches total** (`main` + 61 non-default branches).
- Current inventory after this recovery: **65 branches total**, because three non-default consolidation branches were added.
- The audit compared remote branch history with current `main` and cross-referenced the complete PR history, including PRs whose base was another non-default stable branch.
- Important: an old branch can still appear `ahead` of `main` after a squash/rollup merge because its original commit IDs are not ancestors of `main`. `ahead` alone is therefore **not** treated as evidence of missing work. PR/rollup history and current-tree intent decide the status below.
- Local worktree registrations and unpushed local commits are deliberately **not** covered here. The coding agent should run a separate local `git worktree list` / local-ref audit.
- No default-branch merge was performed by this recovery.

## Status vocabulary

- **DEFAULT** — repository default branch; never mutated by this recovery.
- **OPEN-PR** — already has a current PR targeting `main`; no recovery merge was needed.
- **LANDED-MAIN** — useful patch already merged to `main` through its direct PR. Do not re-merge the old branch even if GitHub reports it ahead.
- **LANDED-VIA-STABLE** — useful patch was merged through a non-default stable/rollup path that ultimately reached `main`. Do not re-merge the old child branch.
- **RECOVERED** — useful work was not in `main` and lacked a live default-destined review surface; it is now represented by one of the three consolidation branches below.
- **SUPERSEDED** — branch/PR was explicitly replaced, contaminated, or closed in favor of a later canonical implementation. Do not merge it.
- **CONSOLIDATION** — new non-default recovery branch intended to become a single coding-agent review surface. Fleet/manual agents must not merge it into `main`.

## Consolidation branches

### `fleet/consolidated/platform-security-data-20260905`

Recovered platform/security/data work:

1. `fleet/stable/tooling-hygiene` — hidden stable rollup from #178.
2. `agent/issue-64-remote-exec-pty-fixture` — PTY/remote-exec fixture and transport policy.
3. `fleet/stable/member-data-privacy` — #195 owner-scoped gym-train privacy/security repair.
4. `agent/issue-98-account-connections` — Account & Connections lifecycle/API/persistence package.
5. `agent/issue-110-import-persistence-boundary` — persistence-boundary simplification.
6. `refactor/remove-unused-unit-of-work` — closed-unmerged #135 cleanup; exhaustive audit confirmed `IUnitOfWork` still existed on `main`, so it was not superseded.

Temporary non-default integration PRs: #203, #204, #205, #206, #207, #208.

#64 conflicted only on shared CI/verifier-routing files. The recovery preserved the tooling-hygiene CI/test-tier expansion, added #64's required checkout history and manifest rows, copied the PTY/transport files, and recorded source head `3b96fa001f5608eac1e6e36b45cc5ea46b068f3e` as the second parent of reconciliation merge `ae6ea4f99109d94c21bbd634de5614987b94ce1c`. No package was dropped to resolve the conflict.

### `fleet/consolidated/ux-member-safety-20260905`

Recovered UX/member-safety work:

1. `fleet/stable/member-safe-ui` — hidden member-safe diagnostics rollup from #188.
2. `agent/issue-95-security-delete-confirmation` — destructive-action confirmation/accessibility package.
3. `agent/issue-96-app-shell-navigation` — app-shell/navigation redesign.
4. `docs/ux-north-star-2026-09-05` — full UX North Star research/report.

Temporary non-default integration PRs: #209, #210, #211, #212.

### `fleet/consolidated/war-core-eval-20260905`

Recovered War core/evaluation work:

1. `fleet/stable/war-derivation-core` — #73 derivation decomposition, previously hidden behind stable integration.
2. `fleet/stable/war-replay-eval` — #91 deterministic replay/evaluation package, previously hidden behind stable integration.

Temporary non-default integration PRs: #201, #202.

## Complete branch index

| Branch | Status | PR / history | Recovery destination / action |
|---|---|---|---|
| `agent/issue-64-remote-exec-pty-fixture` | **RECOVERED** | No prior default-destined PR; source `3b96fa0…`; temporary #204 | `fleet/consolidated/platform-security-data-20260905`; CI/manifest conflict unioned, source recorded as merge parent |
| `agent/issue-73-war-derivation-decomposition` | **RECOVERED** via hidden stable | #196 merged only into `fleet/stable/war-derivation-core` | `fleet/stable/war-derivation-core` → #201 → `fleet/consolidated/war-core-eval-20260905` |
| `agent/issue-95-security-delete-confirmation` | **RECOVERED** | No prior PR for this branch | #210 → `fleet/consolidated/ux-member-safety-20260905` |
| `agent/issue-96-app-shell-navigation` | **RECOVERED** | No prior PR for this branch | #211 → `fleet/consolidated/ux-member-safety-20260905` |
| `agent/issue-98-account-connections` | **RECOVERED** | No prior PR for this branch | #206 → `fleet/consolidated/platform-security-data-20260905` |
| `agent/issue-100-chain-planner` | **OPEN-PR** | #194 open to `main` | Leave for coding-agent review; not duplicated into consolidation |
| `agent/issue-101-home-gym-explorer` | **OPEN-PR** | #200 open to `main` | Leave for coding-agent review; not duplicated into consolidation |
| `agent/issue-103-member-safe-diagnostics` | **RECOVERED** via hidden stable | #188 merged only into `fleet/stable/member-safe-ui` | `fleet/stable/member-safe-ui` → #209 → UX consolidation |
| `agent/issue-110-import-persistence-boundary` | **RECOVERED** | No prior PR for this branch | #207 → platform consolidation |
| `agent/issue-195-gym-train-owner-scope` | **RECOVERED** via hidden stable | #197 merged only into `fleet/stable/member-data-privacy` | `fleet/stable/member-data-privacy` → #205 → platform consolidation |
| `docs/ux-north-star-2026-09-05` | **RECOVERED** | No prior PR for this branch | #212 → UX consolidation |
| `feat/agent-task-leases` | **LANDED-MAIN** | #127 merged | Historical branch; do not re-merge |
| `feat/combat-intel-resolution-core` | **LANDED-MAIN** | #133 merged | Historical branch; do not re-merge |
| `feat/m009-consent-record` | **LANDED-MAIN** | #129 merged | Historical branch; do not re-merge |
| `feat/p0-80-stored-member-key` | **LANDED-MAIN** | #164 merged | Historical branch; do not re-merge |
| `feat/p0-81-combat-intel-persistence` | **LANDED-MAIN** | #159 merged | Historical branch; do not re-merge |
| `feat/p0-81-ffscouter-adapter` | **LANDED-MAIN** | #163 merged | Historical branch; do not re-merge |
| `feat/p0-95-shared-state-components` | **LANDED-MAIN** | #153 merged | Historical branch; do not re-merge |
| `feat/p0-95-theme-centralization` | **LANDED-MAIN** | #161 merged | Historical branch; do not re-merge |
| `feat/p1-69-architecture-test-project` | **LANDED-MAIN** | #165 merged | Historical branch; later #199 continues #69 |
| `feat/pr-evidence-contract` | **LANDED-MAIN** | #120 merged | Historical branch; do not re-merge |
| `feat/required-evidence-classifier` | **LANDED-MAIN** | #119 merged | Historical branch; do not re-merge |
| `feat/shared-member-state-adoption` | **LANDED-MAIN** | #168 merged | Historical branch; do not re-merge |
| `feat/war-objective-persistence-auth` | **LANDED-VIA-STABLE** | #174 → `fleet/stable/war-objectives`; stable rollup #180 merged to `main` | Historical child; do not re-merge |
| `feat/war-scout-provenance` | **LANDED-VIA-STABLE** | #172 → `fleet/stable/ux-proof-provenance`; rollup #176 merged to `main` | Historical child; do not re-merge |
| `fix/align-prod-nginx-conf-name` | **LANDED-MAIN** | #166 merged | Historical branch; do not re-merge |
| `fix/combat-intel-common-invariants` | **LANDED-MAIN** | #139 merged | Historical branch; do not re-merge |
| `fix/import-tenant-ownership` | **SUPERSEDED** | #149 closed unmerged after overlap audit; busy-admission owned by #148 and owner-scoped resume by #150 | Do not merge |
| `fix/internal-war-notify-boundary` | **LANDED-MAIN** | #132 merged | Historical branch; do not re-merge |
| `fix/local-return-url-policy` | **LANDED-MAIN** | #144 merged | Canonical redirect-locality implementation |
| `fix/p0-95-remove-fake-theme-selector` | **LANDED-MAIN** | #157 merged | Historical branch; do not re-merge |
| `fix/p0-131-surfaces-raw-denial` | **LANDED-MAIN** | #155 merged | Historical branch; do not re-merge |
| `fix/p1-103-home-import-error-member-safe` | **LANDED-MAIN** | #162 merged | Historical branch; do not re-merge |
| `fix/p1-103-login-member-safe` | **LANDED-MAIN** | #160 merged | Historical branch; do not re-merge |
| `fix/p1-103-mystats-import-error-member-safe` | **LANDED-MAIN** | #167 merged | Historical branch; do not re-merge |
| `fix/p1-103-player-account-member-safe` | **LANDED-MAIN** | #158 merged | Historical branch; do not re-merge |
| `fix/remove-weather-demo-route` | **LANDED-MAIN** | #141 merged | Historical branch; do not re-merge |
| `fix/security-local-return-urls` | **SUPERSEDED** | Old implementation branch explicitly replaced by current-main #144 | Do not merge |
| `fix/surfaces-cache-outside-webroot` | **LANDED-MAIN** | #143 merged | Historical branch; do not re-merge |
| `fix/xunit-global-using` | **LANDED-MAIN** | #142 merged | Historical branch; do not re-merge |
| `fleet/b/65-warning-baseline` | **SUPERSEDED** | #177 closed unmerged after branch contamination; clean replacement #178 | Do not merge contaminated branch |
| `fleet/b/65-warning-baseline-clean` | **RECOVERED** via hidden stable | #178 merged into `fleet/stable/tooling-hygiene`, not directly to `main` | `fleet/stable/tooling-hygiene` → #203 → platform consolidation |
| `fleet/b/72-warpoller-timeprovider` | **LANDED-VIA-STABLE** | #175 → war-core stable → #182 → war-readiness stable → #185 `main` | Historical child; do not re-merge |
| `fleet/b/89-objective-consumption` | **LANDED-VIA-STABLE** | #179 → `fleet/stable/war-objectives`; #180 merged to `main` | Historical child; do not re-merge |
| `fleet/b-86-opponent-pressure` | **LANDED-VIA-STABLE** | #181 → war-readiness stable; #185 merged to `main` | Historical child; do not re-merge |
| `fleet/b-86-readiness-core` | **LANDED-VIA-STABLE** | #184 → war-readiness stable; #185 merged to `main` | Historical child; do not re-merge |
| `fleet/b-86-travel-availability` | **LANDED-VIA-STABLE** | #183 → war-readiness stable; #185 merged to `main` | Historical child; do not re-merge |
| `fleet/consolidated/platform-security-data-20260905` | **CONSOLIDATION** | Created from exact current `main`; 0 behind after package integration | Coding-agent review surface for platform/security/data recovery; never fleet-merge to default |
| `fleet/consolidated/ux-member-safety-20260905` | **CONSOLIDATION** | Created from exact current `main`; 0 behind after package integration | Coding-agent review surface for UX/member-safety recovery; never fleet-merge to default |
| `fleet/consolidated/war-core-eval-20260905` | **CONSOLIDATION** | Created from exact current `main`; 0 behind after package integration | Coding-agent review surface for War core/evaluation recovery; never fleet-merge to default |
| `fleet/docs-self-improvement-archive` | **SUPERSEDED / CONTENT LANDED** | #173 merged initial archive; later #186 closed because ancestry made review noisy; intended appended content was transplanted onto fresh stable archive and merged via #187/#198 | Do not merge this old noisy branch |
| `fleet/i-91-replay-core` | **RECOVERED** via hidden stable | #190 and repair #192 merged into `fleet/stable/war-replay-eval` | `fleet/stable/war-replay-eval` → #202 → WAR consolidation |
| `fleet/stable/member-data-privacy` | **RECOVERED** | Contained #197/#195 security work not yet in `main` | #205 → platform consolidation |
| `fleet/stable/member-safe-ui` | **RECOVERED** | Contained #188 member-safe diagnostics not yet in `main` | #209 → UX consolidation |
| `fleet/stable/tooling-hygiene` | **RECOVERED** | Contained #178 tooling/browser-crypto/warnings package not yet in `main` | #203 → platform consolidation |
| `fleet/stable/ux-proof-provenance` | **LANDED-MAIN** | Rollup #176 merged to `main` | Historical stable; do not re-merge despite commit-identity divergence |
| `fleet/stable/war-core-simplification` | **LANDED-VIA-STABLE** | #182 merged this stable into war-readiness stable; #185 then merged that rollup to `main` | Historical intermediate stable; do not re-merge |
| `fleet/stable/war-derivation-core` | **RECOVERED** | #196 had merged into this stable, but no stable→default review surface existed | #201 → WAR consolidation |
| `fleet/stable/war-objectives` | **LANDED-MAIN** | Stable rollup #180 merged to `main` | Historical stable; do not re-merge despite commit-identity divergence |
| `fleet/stable/war-replay-eval` | **RECOVERED** | #190/#192 lived here with no stable→default review surface | #202 → WAR consolidation |
| `main` | **DEFAULT** | `270666a030e0473c2891fef9e0fd696a6c0df443` throughout recovery | Untouched; final review/merge remains coding-agent/human only |
| `proof/shared-state-rendered-t2` | **LANDED-VIA-STABLE** | #169 → `fleet/stable/ux-proof-provenance`; #176 merged rollup to `main` | Historical proof branch; do not re-merge |
| `refactor/gym-trains-pass-through` | **LANDED-MAIN** | #134 merged | Historical branch; do not re-merge |
| `refactor/remove-unused-unit-of-work` | **RECOVERED** | #135 closed unmerged; audit confirmed current `main` still contained `IUnitOfWork` | #208 → platform consolidation |
| `test/verifier-dependency-regression` | **SUPERSEDED** | #123 closed unmerged; superseded by #125 canonical verifier package | Do not merge |

## Coding-agent review order

1. Review the three **CONSOLIDATION** branches as coherent review units, not the original child/stable branches.
2. Continue reviewing existing open PRs #194 and #200 independently.
3. Do not infer missing work from an old branch being `ahead` until this inventory's PR/rollup classification has been checked.
4. For each consolidation PR, rerun exact-head CI and inspect the union rather than trusting child-branch proof from an older base.
5. Perform the separate local-worktree audit before deleting any local branch/worktree.
6. Never delete an original remote recovery source merely because it is represented here until the coding-agent has accepted the consolidation and proven the source is safely redundant.

## Durable workflow rule exposed by this incident

A useful branch is **not** durably handed off merely because it was pushed, and a child PR is **not** durably handed off merely because GitHub labels it merged into a non-default stable branch. Before a run ends, useful work must be in exactly one visible terminal state:

1. represented by an open PR that is ultimately destined for the repository default branch, directly or through an open rollup PR;
2. proven incorporated/superseded by a current default-destined review surface, with that relationship recorded; or
3. explicitly abandoned with the useful commits assessed and the reason recorded.

A non-default stable branch that contains useful commits absent from the default branch must not sit indefinitely without a current default-destined rollup/review surface.
