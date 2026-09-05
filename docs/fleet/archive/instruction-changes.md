# Fleet Instruction Change Archive

Append-only durable prompt/instruction history. GitHub issue #171 is the live tracker/index. Repository LOCK issues remain canonical coordination state.

## 2026-09-05T03:42+02:00 — always-working discovery and Steward loop

FLEET-PROMPT-CHANGE | timestamp=2026-09-05T03:42+02:00

automation: Hourly Primary Implementation Lane I; Hourly Secondary Work-Package Builder Lane B; Hourly PR Rescue, Stable Integration & Proof Lane A; Hourly PZ + IMEME Implementation, Proof & Stable Integration M1; Hourly Fleet Audit, Discovery, Archive & Steward

evidence: repeated under-use of deterministic tooling; lanes idling/re-reviewing when implementation gates were saturated; excessive micro-PR/issue fragmentation; need for durable research/discovery output and measured self-improvement

problem: fleet treated implementation availability as the main source of useful work and lacked a durable evaluator→optimizer feedback loop

change: added always-working fallback ladder through repository discovery, external research, product strategy, security red-team, harness/eval work and bounded Fleet Steward prompt improvement; added #170/#171 issue trackers

invariants: preserved

expected-effect: fewer no-op runs, deeper discovery, more actionable research, higher tool utilization, better conversion of research into canonical work, evidence-backed prompt tuning

rollback: remove fallback/persona/Steward additions from active automation prompts and restore pre-03:42 prompt definitions while preserving constitutional coordination and merge boundaries

evaluation: pending

## 2026-09-05T03:47+02:00 — Git-versioned archive becomes durable source

FLEET-PROMPT-CHANGE | timestamp=2026-09-05T03:47+02:00

automation: Hourly Fleet Audit, Discovery, Archive & Steward

evidence: #170/#171 were useful live trackers but issue comments alone are awkward for versioned diff/history and future PI/eval ingestion; Gerome requested Git-versioned archive files with GitHub issues retained as live tracker

problem: archive durability/provenance currently depends on issue comments rather than repository-versioned files

change: Steward must write material activity snapshots to `docs/fleet/archive/activity/YYYY-MM.md` and prompt changes to `docs/fleet/archive/instruction-changes.md` on claimed non-default fleet branches first; #170/#171 become concise live tracker/index surfaces pointing to the Git branch/PR/path; prompt-change Git entry must precede `automations.update`

invariants: preserved

expected-effect: diffable/versioned fleet history, easier rollback/audit, cleaner live issues, straightforward future PI/database ingestion

rollback: return #170/#171 to primary archive role while retaining the Git files as historical seed; do not delete prior Git history

evaluation: pending
