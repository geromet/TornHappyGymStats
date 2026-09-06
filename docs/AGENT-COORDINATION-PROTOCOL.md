# Agent coordination protocol

This document defines the durable coordination protocol for concurrent fleet and Codex work in this repository. GitHub issue **#140** is the live transport/state log. Repository/PR/issue state and the latest valid protocol transitions win over stale prose or historical handoffs.

The protocol is intentionally designed for abrupt worker termination. A run may disappear at any point; the last acknowledged state must still identify ownership, branch, head, PR and the next safe action.

## 1. State machine

| State | Meaning | Active fleet lease? |
|---|---|---:|
| 🟢 **OPEN** | default/unowned; or unfinished work deliberately released | No |
| 🟡 **ASSIGNING** | phase-1 assignment request / SYN | Yes |
| 🔵 **ASSIGNED** | phase-2 assignment acknowledgement / ACK | Yes |
| 🛠️ **WORKING @ branch** | mutation is in progress on an exact branch/head | Yes |
| 🧪 **WAITING ON PR CR** | coherent package is waiting on Codex code/final review | No |
| 🆘 **WAITING ON ADVISOR** | fleet needs Codex to investigate/repair coordination or topology | No |
| ✅ **FINISHED** | issue/package is truly completed and disposition/default incorporation is verified | No |

Only ASSIGNING, ASSIGNED and WORKING consume the five-work fleet ceiling.

## 2. Wire record

Every transition is a **new #140 comment**. Never mutate an old state comment into a new state.

Minimum record:

```text
<emoji> <STATE> | seq=<n> | run=<token> | lane=<lane> |
issue/package=<id> | branch=<name-or-none> | head=<sha-or-none> |
pr=<number-or-none> | prev=<prior-state/comment-id> |
ts=<ISO8601> | note=<short result>
```

Assignment ACK records also include:

```text
ack=<ASSIGNING-comment-id>
```

Advisor records also include:

```text
fingerprint=advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

Unknown values are written explicitly as `none` or `unknown`. Load-bearing metadata is never omitted merely to make a comment shorter.

## 3. Connection / sequence semantics

A unique `run=<token>` is the connection identifier. `seq` is monotonically increasing **within that run**.

1. First mutable intent is `🟡 ASSIGNING seq=1`, except the lease-free saturated-advisor control-plane path defined in §7.
2. The winner of the overlap check appends `🔵 ASSIGNED seq=2` and acknowledges the ASSIGNING comment ID.
3. Branch selection/creation is recorded as `🛠️ WORKING` before useful mutation.
4. Every remote branch-head change is followed by another WORKING record with the next sequence number and exact new head.
5. Waiting and terminal states append another sequence record; they never rewrite history.

Before retrying a write after an ambiguous response, reread recent #140 comments. If the exact `run+seq` already exists, treat the retry as acknowledged and do not duplicate it.

A sequence gap, duplicate conflicting `run+seq`, contradictory latest states, or branch/head mismatch fails closed until reconciled.

## 4. Assignment handshake

### Phase 1 — 🟡 ASSIGNING / SYN

Before any mutation:

- refresh #140 body + recent transitions;
- refresh the target issue/PR;
- discover current default branch/head;
- refresh every branch/PR head involved;
- choose the exact intended branch name;
- append ASSIGNING with exact issue/package/seam and observed heads.

Reread #140 immediately and reconstruct the latest valid state for every relevant run. If any materially overlapping run is already `🔵 ASSIGNED` or `🛠️ WORKING`, the newcomer loses and must append 🟢 OPEN for its own run/scope; a new ASSIGNING record never supersedes an active owner. Only when no active owner overlaps do competing ASSIGNING records race, and then the earlier overlapping ASSIGNING GitHub comment ID wins.

### Phase 2 — 🔵 ASSIGNED / ACK

The winner appends a new ASSIGNED record referencing the ASSIGNING comment ID. Only then may the run mutate GitHub state.

The loser appends 🟢 OPEN for its run/scope and chooses independent work. Do not repeatedly compete for the same scope.

## 5. Branch crash-recovery record

Immediately after a branch is created or selected, and before useful mutation, append:

```text
🛠️ WORKING @ branch | ... | branch=<exact-name> | head=<exact-sha> | ...
```

After each push/ref mutation, append the new head before proceeding to unrelated work.

This rule is deliberately redundant with Git itself: the LOCK tells a future agent **which** branch was owned and what head the previous worker believed it had. Git then verifies whether that belief is still current.

Branch mutations use compare-and-swap semantics. If current remote head differs from expected head, stop and reconcile. Never force-push through a coordination race.

## 6. Waiting states release the fleet lease

### 🧪 WAITING ON PR CR

Use when the coherent review unit is complete enough for Codex review/final review. Include:

- PR number;
- exact head/base;
- relevant issue/package scope;
- required vs observed evidence;
- automatic review/scan status;
- handoff URL/marker if already sent.

Do not keep an implementation lease merely because Codex review is pending.

### 🆘 WAITING ON ADVISOR

Use when the fleet should stop trying to self-heal a coordination/topology problem and ask Codex to investigate and repair it.

Before posting, construct a stable fingerprint and search #140. An unresolved identical fingerprint suppresses another request.

## 7. Codex advisor safety valve

Before concluding there is no work, before repeating unchanged investigation, and before acquiring a sixth active lease, inspect the latest state per run plus current branches/PRs/open issues.

Escalate once when **any** applies:

1. open **non-documentation** issues remain, but the agent concludes there is no safe runnable implementation/rescue/integration work;
2. the same issue/branch/PR/anomaly would be investigated or attempted again without material head/state/evidence change;
3. active fleet leases already exceed five or a new assignment would exceed five;
4. stale/contradictory/cut-off state, hidden useful branch, missing PR/review path, supersession ambiguity, or branch/LOCK disagreement cannot be reconciled cheaply and confidently.

Fingerprint format:

```text
advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

When five active leases already exist, advisor dispatch uses an explicit **lease-free control-plane exception** so recovery does not create a prohibited sixth lease. After read-only state reconstruction and fingerprint deduplication, an otherwise unassigned recovery run may append `🆘 WAITING ON ADVISOR` directly as its first transition (`seq=1`, `prev=none`) and post the single matching `@codex` advisor request. This exception authorizes only those #140 control-plane comments. It does **not** authorize branch, PR, issue-body, code, review, merge, or other repository mutation; any such work still requires the normal ASSIGNING → ASSIGNED handshake after capacity is available.

The advisor request should be a single account-authored #140 comment containing `@codex` and must ask Codex to:

- inspect live GitHub state, not stale handoffs;
- reconstruct latest valid protocol state per run;
- inspect current branches, open/closed PRs and issue state;
- preserve unique useful commits;
- repair stale/contradictory state and branch/PR topology where safe;
- continue or finish useful work when appropriate;
- use this protocol for its own mutations;
- respect repository protections and exact-head semantics;
- report concrete repairs or blockers.

Do not repeatedly dispatch the same unresolved fingerprint.

## 8. Review-unit economy

The protocol separates **implementation leases** from **review surfaces** so work can accumulate without creating branch/PR explosions.

For Torn:

- three open fleet-owned default-destined PRs is the normal operating cap;
- five is the emergency ceiling, not a target;
- prefer persistent feature/work-package/integration branches and existing draft/open PRs across multiple hourly runs;
- do not create a new default-destined PR merely because one run produced a small diff;
- a new default-destined PR should normally represent a complete meaningful feature/work-package, multiple tightly related acceptance slices/issues, a coherent integration rollup, or an urgent independently reviewable stop-line/security repair;
- parallel child branches exist only when isolation materially helps concurrency and should feed a claimed non-default integration branch when compatible;
- exact branch/head WORKING records are mandatory so persistent branches remain discoverable after cutoffs;
- normally make at most one new Codex final-review/merge handoff per repository per hour, preferably less.

A changed head on an accumulating PR is not, by itself, a reason to request another Codex final review.

## 9. FINISHED means the issue is actually done

`✅ FINISHED` is not equivalent to "pushed", "PR opened", "tests green", or "handed to Codex".

Use FINISHED only when the issue/package's completion semantics are satisfied and either:

- required default incorporation is verified; or
- the issue has an explicit completed/not-planned terminal disposition that does not require default incorporation.

Record the final PR/default SHA/disposition.

## 10. State reconstruction algorithm

A recovery/advisor agent should:

1. read #140 body and recent comments;
2. parse recognized protocol records;
3. group records by `run`;
4. order by `seq` and validate `prev`/ACK references;
5. choose the highest valid sequence as that run's current state;
6. map unreleased legacy `🔒 CLAIM` records only under the migration rules below;
7. count active leases from latest ASSIGNING/ASSIGNED/WORKING states;
8. verify every WORKING branch still exists and compare recorded head with live remote head;
9. correlate recorded PRs with live PR bases/heads/state;
10. repair or escalate contradictions rather than guessing ownership.

## 11. Legacy migration

Do not rewrite historical comments.

- unreleased old `🔒 CLAIM` with an exact branch is treated as legacy 🛠️ WORKING;
- unreleased old `🔒 CLAIM` without an exact branch is treated as legacy 🔵 ASSIGNED;
- old `🔓 RELEASED` is non-active;
- convert legacy state into the new protocol only when that scope is touched or reconciled.

## 12. Examples

### Normal assignment

```text
🟡 ASSIGNING | seq=1 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
prev=OPEN | ts=... | note=footer disclosure + adjacent account privacy seam
```

```text
🔵 ASSIGNED | seq=2 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
prev=ASSIGNING/<comment-id> | ack=<comment-id> | ts=... | note=overlap check won
```

```text
🛠️ WORKING @ branch | seq=3 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<branch-sha> | pr=#235 |
prev=ASSIGNED/<comment-id> | ts=... | note=branch selected; mutation begins
```

### Codex review wait

```text
🧪 WAITING ON PR CR | seq=8 | run=ux-abc123 | lane=UX | issue/package=#98,#230 |
branch=feature/account-privacy | head=<exact-head> | pr=#235 |
prev=WORKING/<comment-id> | ts=... | note=coherent package complete; current-head evidence recorded
```

### Advisor escalation

```text
🆘 WAITING ON ADVISOR | seq=1 | run=steward-def456 | lane=STEWARD |
issue/package=coordination | branch=none | head=none | pr=none |
prev=none | ts=... |
fingerprint=advisor:geromet/TornHappyGymStats:active-leases:5:<state-hash> |
note=@codex lease-free saturated-control-plane escalation; inspect and repair stale/cut-off leases and branch/PR topology
```

The goal is not ceremony. The goal is that an interrupted worker leaves enough durable, ordered evidence for the next worker or Codex advisor to recover safely without inventing state.