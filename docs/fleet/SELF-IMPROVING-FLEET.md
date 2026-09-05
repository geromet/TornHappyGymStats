# Self-Improving Fleet Loop

## Core idea

The fleet should not have a normal terminal state called **nothing to do**. When direct implementation is blocked, saturated, claimed, or already complete, an agent should move down a deliberate fallback ladder and still produce the strongest safe durable result available.

The operating loop is:

1. **Finish engineering work** — code, CI repair, proof, review findings, integration, stable-branch consolidation.
2. **Implement ready canonical packages** within WIP/PR pressure limits.
3. **Prepare and compact backlog** into fewer substantial work packages.
4. **Repository discovery** — correctness, security, UX, performance, maintainability, architecture, dependency, observability, tooling and evidence gaps.
5. **External research** — current authoritative docs, standards, advisories, ecosystem/competitor tools, APIs, libraries and relevant research.
6. **Product invention** — a small number of evidence-backed features/workflows with clear user value and implementation seams.
7. **Harness/eval improvement** — deterministic tests, fixtures, browser/component proof, real-tool compatibility, CI/evidence tooling, reproducible research scaffolds.
8. **Fleet self-improvement** — evaluate recent fleet traces/outcomes and make bounded evidence-backed prompt changes.

Agents may switch **internal operating persona** as they move through the ladder — Engineer, Investigator, Researcher, Product Strategist, Security Red-Team, Harness/Eval Engineer, Fleet Steward. They must not change Gerome's global ChatGPT Personality setting.

## Research quality bar

Research is real work, not filler. A research run should start with a bounded question or hypothesis, reconcile current repository/issues/PR state first, use authoritative current sources for changing facts, and distinguish:

- repository evidence;
- sourced external fact;
- inference;
- product idea.

Prefer one deep durable report or canonical issue update over issue spam. Security research needs a concrete effect chain or negative-control plan. Product research should explain user workflow/value, ecosystem evidence, the missing capability, implementation boundary, and why the product should own it.

A research-only run is successful when it materially deepens a canonical package/report, creates one genuinely distinct evidence-backed package, improves a harness/eval, or establishes a meaningful bounded negative finding.

## Three feedback loops

### Minutes / hours — engineering loop

`code → test/prove → repair → integrate → stable branch`

This loop optimizes delivery quality and drains active PR pressure.

### Hours / days — discovery loop

`inspect → research → synthesize → create/compact canonical work package → implement`

This loop prevents the backlog from becoming stale and gives otherwise-idle agents useful work.

### Days / weeks — fleet improvement loop

`archive traces/outcomes → identify repeated behavior problem → define eval target → make smallest prompt change → observe subsequent runs → keep/revise/rollback`

This is an evaluator→optimizer loop. The goal is not more activity; it is better throughput, proof quality, coordination, tool use, research conversion and review burden.

## Constitutional invariants

Fleet self-improvement is bounded. Without explicit Gerome approval, no automatic prompt change may weaken:

- no fleet merge into a repository default branch;
- Gerome/coding-agent ownership of final default review/merge;
- actual-default-branch discovery;
- repository LOCK + two-phase claim + earlier-comment-ID race winner + collision backoff + same-claim RELEASED + head CAS;
- outside-contributor protections;
- default five independent-package and five open fleet-owned PR ceilings;
- fleet merges only into explicitly verified non-default stable/integration branches;
- no unavailable secrets, sudo/admin, interactive SSH/passkey, production/operator-only or destructive external/game-state actions;
- truthful evidence / no invented verification;
- genuinely non-deterministic product choices stay human.

## Durable archive topology

Git is the durable versioned archive. GitHub issues are the live tracker/index.

### Git-versioned files

- `docs/fleet/archive/activity/YYYY-MM.md` — append-only compact snapshots of material fleet outcomes.
- `docs/fleet/archive/instruction-changes.md` — append-only prompt/instruction changes, evidence, expected effect, evaluation and rollback.
- this document — architecture and invariants for the loop.

Archive files should be changed on fleet-owned non-default branches and integrated through the normal stable/rollup workflow. Do not mutate the default branch directly.

### Live GitHub trackers

- `#170` — live activity archive/index. Keep a short current summary and links/pointers to the corresponding Git archive entry/branch/PR.
- `#171` — live instruction-change/self-improvement tracker. Record current proposed/applied/evaluated changes and point to the Git-versioned changelog entry.
- repository LOCK issues remain the canonical live coordination/ownership source; archive files never replace LOCK state.

The issue trackers are allowed to be lossy/current. Git history is the durable provenance layer.

## Archive write protocol

For a material fleet archive interval:

1. reconcile recent LOCK releases, PR/issue/stable activity, CI/evidence, research and prompt changes;
2. append a compact `FLEET-SNAPSHOT` entry to the appropriate monthly Git archive file on a claimed fleet-owned non-default branch;
3. open/update the relevant archive/stable PR if needed;
4. add or update a concise pointer in `#170` with the Git path/branch/PR and current headline;
5. do not mirror every tool call or claim comment.

Suggested activity entry:

```text
FLEET-SNAPSHOT | period=<ISO interval>
repos: <material repo summaries>
shipped: <PRs/branches/issues/tests/proof>
research: <material discoveries or none>
coordination: <collisions/WIP/stable topology>
self-improvement: <instruction-change refs or none>
next-pressure: <most useful unresolved system-level pressure>
```

For instruction changes, **write the Git changelog entry before applying the automation prompt mutation**, then update `#171` as the live tracker/index.

Suggested change entry:

```text
FLEET-PROMPT-CHANGE | timestamp=<ISO>
automation: <title/id>
evidence: <recent runs / PRs / LOCK refs / archive refs>
problem: <concrete repeated failure mode>
change: <concise before→after behavior>
invariants: preserved
expected-effect: <what should improve>
rollback: <what to restore if regressions appear>
evaluation: <pending / improved / regressed / mixed>
```

## Steward cadence

The Fleet Steward should evaluate automation prompts at most once every four hours unless there is an urgent coordination/safety defect. It should inspect exact current automation definitions, recent archive history and live GitHub state, then make the smallest evidence-backed prompt change that addresses a repeated concrete problem.

Prompt changes should be reversible and evaluated against subsequent runs. If throughput, safety, evidence quality, coordination or review burden regresses, record the evaluation and rollback/revise.

## Future PI / eval harness

The Git + issue archive is intentionally simple enough to migrate later into a PI/eval/telemetry harness and database. Useful future metrics include:

- productive-output per run;
- no-op rate;
- collision/backoff rate;
- proof-closure rate;
- tool-utilization rate;
- issue→canonical-package compression;
- child-PR→stable-rollup compression;
- research→implementation conversion;
- stable-branch integration latency;
- regressions after prompt changes;
- human review burden per delivered work package.

Once structured traces and metrics exist, the Steward should optimize against explicit eval targets instead of relying mainly on qualitative judgment.
