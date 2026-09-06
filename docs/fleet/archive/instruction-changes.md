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

## 2026-09-05T22:50+02:00 — documentation and agent-instruction truth loop

FLEET-PROMPT-CHANGE | timestamp=2026-09-05T22:50+02:00

automation: Hourly Fleet Audit, Discovery, Archive & Steward; Hourly PR Rescue, Stable Integration & Proof Lane A; Hourly Security Boundary Scout; Hourly UX Evidence Scout; Hourly PR Acceptance Validator

evidence: Torn exhaustive Markdown audit #223/#224 reviewed all 33 tracked Markdown files at exact default head and found 10 verified stale/inaccurate/contradictory items across 9 files, including a verifier-writing skill that contradicted the executable manifest-routing contract, `MILESTONES.md` reasserting gitignored `workspace/V2/` as authority, and a human queue that still described already-completed branch-protection work

problem: fleet had a truthful-evidence invariant but no continuous mechanism ensuring current documentation, runbooks, tracker prose and agent instructions remain truthful as code/issues/workflows evolve; agents can therefore follow stale commands, routes, statuses or authority pointers even while implementation evidence itself is honest

change: add a continuous documentation/instruction truth loop. Every enabled lane must verify any current doc/instruction claim it relies on or changes against live default code, executable contracts, current issues/PRs and the repository LOCK; historical/archive/research snapshots must be preserved and clearly classified rather than rewritten merely for age. The Steward periodically rotates bounded repo-wide tracked-Markdown/agent-instruction audits, maintains one canonical drift inventory/tracker instead of issue spam, and re-enumerates after remediation. Time-sensitive statuses should be removed, generated, or unmistakably snapshot-dated. Contradictory agent/workflow authority is high priority.

invariants: preserved

expected-effect: fewer stale-command and stale-authority failures, less contradictory agent behavior, lower rediscovery/rework, clearer distinction between live guidance and historical provenance, and a durable audit trail for documentation truth

rollback: remove the per-lane DOC/INSTRUCTION TRUTH rules and Steward periodic truth-audit section while preserving #223/#224 as historical evidence; retain all coordination, evidence, merge-authority, archive and safety invariants

evaluation: pending
