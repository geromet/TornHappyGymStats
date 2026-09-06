# Agent coordination protocol

This document defines the durable coordination protocol for concurrent fleet and Codex work in this repository. GitHub issue **#140** is the live transport/state log. Repository/PR/issue state and the latest valid protocol transitions win over stale prose or historical handoffs.

The protocol is designed for abrupt worker termination. Active ownership has no TTL: the last acknowledged state must remain reconstructable even when a worker disappears.

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
pr=<number-or-none> | base=<ref-or-none> | base_sha=<sha-or-none> |
prev=<prior-state/comment-id> | ts=<ISO8601> | note=<short result>
```

For non-PR states, `base=none` and `base_sha=none` are valid. PR-facing WAITING/proof transitions must record the exact base ref and SHA that the evidence covered.

Assignment ACK records also include:

```text
ack=<ASSIGNING-comment-id>
```

Advisor records also include:

```text
fingerprint=advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

Unknown values are explicit. Load-bearing metadata is never omitted merely to shorten a comment.

## 3. Complete state reconstruction

Active records have no TTL, so an arbitrary recent-comment window is never authoritative for ownership or lease counting.

Before admitting mutable work, resolving a collision, declaring no work, or dispatching an advisor, do one of the following:

1. paginate **all** #140 comments and parse all recognized protocol/legacy records; or
2. consult a mechanically maintained durable index that is proven complete with respect to all #140 protocol comments.

Then:

1. group records by `run`;
2. order each run by `seq` and validate `prev`/ACK references;
3. reject duplicate conflicting `run+seq`, sequence gaps, and contradictory state;
4. select the highest valid transition for every run;
5. apply the legacy migration rules in §11;
6. count active leases from latest ASSIGNING/ASSIGNED/WORKING states;
7. verify recorded WORKING branches/heads against live GitHub state when relevant.

Recent comments may be used as a fast view only after complete reconstruction. Before retrying an ambiguous write, reread enough of #140 to prove whether the exact `run+seq` already exists; if it does, do not duplicate it.

## 4. Assignment handshake and capacity admission

### Phase 1 — 🟡 ASSIGNING / SYN

Before any mutation:

- reconstruct complete #140 state as defined in §3;
- refresh the target issue/PR and dependencies;
- discover current default branch/head;
- refresh every branch/PR head involved;
- choose the exact intended branch name;
- append ASSIGNING with exact issue/package/seam and observed heads.

Immediately reconstruct complete state again. Admission has two independent gates.

### Gate A — ownership collision

If any materially overlapping run is already `🔵 ASSIGNED` or `🛠️ WORKING`, the newcomer loses and must append 🟢 OPEN for its own run/scope. A new ASSIGNING record never supersedes an active owner.

If multiple otherwise-eligible ASSIGNING records overlap each other, only the earliest GitHub comment ID remains eligible; later overlapping candidates lose.

### Gate B — global five-lease capacity

Capacity arbitration is global, not scope-local. This prevents two non-overlapping candidates from concurrently observing four active leases and both becoming a sixth lease.

1. Count incumbent latest `ASSIGNED` + `WORKING` runs. Call this `I`.
2. Compute `slots = max(0, 5 - I)`.
3. Take every latest ASSIGNING record that survived Gate A, sort them by GitHub comment ID ascending, and admit only the earliest `slots` candidates.
4. An ASSIGNING candidate may append ACK only if it is in that admitted prefix. Every later candidate appends 🟢 OPEN/backoff and must not mutate.

Because all contenders use the same durable comment-ID ordering and complete state, non-overlapping admission races resolve deterministically at the five-lease ceiling.

### Phase 2 — 🔵 ASSIGNED / ACK

Only a candidate that passed both gates appends ASSIGNED referencing the ASSIGNING comment ID. Only then may repository mutation begin.

The loser appends 🟢 OPEN for its run/scope and chooses independent work. Do not repeatedly compete for the same unchanged scope.

## 5. Branch crash-recovery record

Immediately after a branch is created or selected, and before useful mutation, append WORKING with the exact branch/head. After each remote branch-head mutation, append the new head before proceeding to unrelated work.

Branch mutations use compare-and-swap semantics. If current remote head differs from expected head, stop and reconcile. Never force-push through a coordination race.

## 6. Waiting states release the fleet lease

### 🧪 WAITING ON PR CR

Use when a coherent review unit is complete enough for Codex review/final review. Record:

- PR number;
- exact head SHA;
- exact base ref and base SHA;
- relevant issue/package scope;
- required versus observed evidence;
- automatic review/scan status;
- handoff marker if already sent.

Do not keep an implementation lease merely because Codex review is pending.

### 🆘 WAITING ON ADVISOR

Use when the fleet should stop trying to self-heal a coordination/topology problem and ask Codex to investigate and repair it. Construct a stable fingerprint and search complete #140 state first; an unresolved identical fingerprint suppresses another request.

## 7. Codex advisor safety valve

Before concluding there is no work, before repeating unchanged investigation, and before acquiring a sixth active lease, inspect complete latest state plus current branches/PRs/open issues.

Escalate once when any applies:

1. open non-documentation issues remain but there is no safe runnable implementation/rescue/integration work;
2. the same issue/branch/PR/anomaly would be retried without material state/evidence change;
3. active leases already exceed five or a new assignment would exceed five;
4. stale/contradictory/cut-off state, hidden useful work, missing review paths, supersession ambiguity, or branch/LOCK disagreement cannot be reconciled cheaply.

Fingerprint format:

```text
advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

When five active leases already exist, advisor dispatch uses a **lease-free #140-only control-plane exception**. After complete read-only reconstruction and fingerprint deduplication, an otherwise unassigned recovery run may append `🆘 WAITING ON ADVISOR` directly as its first transition (`seq=1`, `prev=none`) and post the single matching `@codex` advisor request. This exception authorizes only those #140 control-plane comments; branch, PR, issue-body, code, review, merge, or other repository mutation still requires the normal ASSIGNING → ASSIGNED admission once capacity is available.

Do not repeatedly dispatch the same unresolved fingerprint.

## 8. Review-unit economy

Reviewer cost is a real resource. For Torn:

- three open fleet-owned default-destined PRs is the normal operating cap;
- five is the emergency ceiling, not a target;
- prefer persistent feature/work-package/integration branches and existing review surfaces;
- do not create a new default-destined PR merely because one run produced a small diff;
- parallel child branches are only for useful isolation and should feed a claimed non-default integration branch when compatible;
- normally make at most one new Codex final-review/merge handoff per repository per hour.

A changed head on an accumulating PR is not, by itself, a reason to request another final review.

## 9. FINISHED means the issue is actually done

Use FINISHED only when completion semantics are satisfied and either required default incorporation is verified or an explicit completed/not-planned terminal disposition requires no default incorporation. A pushed branch, open PR, green CI run, or non-default child merge is not FINISHED.

## 10. Recovery algorithm

A recovery/advisor agent should reconstruct complete state per §3, verify every active WORKING branch/head, correlate PR bases/heads/state, preserve unique useful commits, and repair or escalate contradictions instead of guessing ownership.

## 11. Legacy migration

Do not rewrite historical comments.

- unreleased old `🔒 CLAIM` with an exact branch is legacy 🛠️ WORKING;
- unreleased old `🔒 CLAIM` without an exact branch is legacy 🔵 ASSIGNED;
- old `🔓 RELEASED` is non-active;
- convert legacy state into the new protocol only when that scope is touched or reconciled.

## 12. Examples

### Normal assignment

```text
🟡 ASSIGNING | seq=1 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
base=none | base_sha=none | prev=OPEN | ts=... | note=account privacy seam
```

```text
🔵 ASSIGNED | seq=2 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
base=none | base_sha=none | prev=ASSIGNING/<comment-id> | ack=<comment-id> |
ts=... | note=collision and global-capacity admission won
```

### Codex review wait

```text
🧪 WAITING ON PR CR | seq=8 | run=ux-abc123 | lane=UX | issue/package=#98,#230 |
branch=feature/account-privacy | head=<exact-head> | pr=#235 |
base=main | base_sha=<exact-main-sha> | prev=WORKING/<comment-id> |
ts=... | note=coherent package complete; exact-head evidence recorded
```

### Saturated advisor escalation

```text
🆘 WAITING ON ADVISOR | seq=1 | run=steward-def456 | lane=STEWARD |
issue/package=coordination | branch=none | head=none | pr=none |
base=none | base_sha=none | prev=none | ts=... |
fingerprint=advisor:geromet/TornHappyGymStats:active-leases:5:<state-hash> |
note=@codex lease-free saturated-control-plane escalation; inspect and repair state/topology
```

The goal is not ceremony. The goal is that an interrupted worker leaves enough durable, ordered evidence for the next worker or Codex advisor to recover safely without inventing state.