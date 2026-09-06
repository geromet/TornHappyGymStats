# Agent coordination protocol

This document defines the durable coordination protocol for concurrent fleet and Codex work in this repository. GitHub issue **#140** is the live transport/state log. Repository/PR/issue state and the latest valid protocol transitions win over stale prose or historical handoffs.

The protocol is designed for abrupt worker termination. Active ownership has no TTL: the last acknowledged state must remain reconstructable even when a worker disappears.

## 1. State machine

| State | Meaning | Active fleet lease? |
|---|---|---:|
| 🟢 **OPEN** | default/unowned; or unfinished work deliberately released | No |
| 🟡 **ASSIGNING** | phase-1 assignment request / SYN; consumes a lease only if admitted by §4's deterministic prefix | Conditional |
| 🔵 **ASSIGNED** | phase-2 assignment acknowledgement / ACK | Yes |
| 🛠️ **WORKING @ branch** | mutation is in progress on an exact branch/head | Yes |
| 🧪 **WAITING ON PR CR** | coherent package is waiting on Codex code/final review | No |
| 🆘 **WAITING ON ADVISOR** | fleet needs Codex to investigate/repair coordination or topology | No |
| ✅ **FINISHED** | issue/package is truly completed and disposition/default incorporation is verified | No |

For capacity accounting, the active set is: every latest ASSIGNED/WORKING record plus only those latest ASSIGNING records that are in the deterministic admitted prefix defined in §4. A losing/unadmitted SYN is never an active lease even if its worker is cut off before it can append OPEN. This preserves the five-work ceiling without requiring cleanup to run.

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
ack=<ASSIGNING-comment-id> | eligibility=clear |
gate_snapshot=<sha256-of-current-gate-input>
```

ASSIGNING records include `gate_snapshot=<sha256-of-pre-SYN-gate-input>`.

Advisor records also include:

```text
fingerprint=advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

Unknown values are explicit. Load-bearing metadata is never omitted merely to shorten a comment.

## 3. Complete state reconstruction

Active records have no TTL, so an arbitrary recent-comment window is never authoritative for ownership or lease counting.

Before admitting mutable work, resolving a collision, declaring no work, or dispatching an advisor, do one of the following:

1. paginate **all** #140 comments and parse every recognized new-protocol transition **and every recognized legacy CLAIM/RELEASE record**; or
2. consult a mechanically maintained durable index proven complete and equivalent with respect to **all recognized protocol and legacy records** in #140.

Then:

1. group new-protocol records by `run`;
2. order each run by `seq` and validate `prev`/ACK references;
3. reject duplicate conflicting `run+seq`, sequence gaps, and contradictory state;
4. bind the run to the GitHub author of its first valid transition and reject
   later normal transitions by any other author;
5. select the highest valid transition for every run;
6. apply the legacy migration rules in §11 to all legacy records;
7. compute the active lease set using §1 and §4 admission semantics;
8. verify recorded WORKING branches/heads against live GitHub state when relevant.

Another actor never releases, advances, or repairs a run by forging its next
sequence; recovery uses a new run token. The only cross-actor operation is the
ownership-neutral, idempotent advisor dispatch-candidate PATCH in §7.

Recent comments may be used as a fast view only after complete reconstruction. Before retrying an ambiguous write, reread enough of #140 to prove whether the exact `run+seq` already exists; if it does, do not duplicate it.

## 4. Assignment handshake and capacity admission

### Phase 1 — 🟡 ASSIGNING / SYN

Before any repository mutation:

- reconstruct complete #140 state as defined in §3;
- refresh the target issue/PR and dependencies;
- discover current default branch/head;
- refresh every branch/PR head involved;
- choose the exact intended branch name.

Before appending ASSIGNING, apply the eligibility gate: every authoritative
dependency for the intended scope must be satisfied and every applicable stop
gate must be cleared. A blocked scope is ineligible even when ownership and global
capacity are available; it must not append ASSIGNING or enter Gate A/Gate B. Fail
closed until the dependency is satisfied or the authoritative issue disposition
explicitly removes the gate.

Canonicalize the gate input as UTF-8/LF lines formatted
`number|state|updated_at|dependency-status|stop-gate-status`: target line first,
then dependency lines sorted by numeric issue number, with no trailing newline.
Record its lowercase SHA-256 as `gate_snapshot=` on the SYN.

Only an eligible scope appends ASSIGNING with exact issue/package/seam and
observed heads.

ASSIGNING is a durable SYN request, but it becomes an **active lease only if it wins both admission gates below**. This distinction is necessary because comment creation itself is not an atomic capacity reservation and a losing worker may be cut off before cleanup.

Immediately reconstruct complete state again. Admission has two independent gates.

Refresh the target issue/PR and its authoritative dependencies/stop gates again
after the SYN and immediately before evaluating admission. If the scope became
blocked, it is no longer eligible, must not ACK, and should append OPEN/backoff
when able. If the canonical gate input differs from the SYN snapshot for any
reason, that SYN is permanently ineligible even if the gate later returns to a
clear state. A winner's ASSIGNED/ACK durably records `eligibility=clear` and the
fresh matching `gate_snapshot=`; absence of that ACK is never interpreted as a
successful post-SYN decision.

### Gate A — ownership collision

If any materially overlapping run is already `🔵 ASSIGNED` or `🛠️ WORKING`, the newcomer loses and must not mutate. It should append 🟢 OPEN when able, but correctness does not depend on that cleanup because the losing SYN is not admitted.

If multiple otherwise-eligible and permanently admitted ASSIGNING records overlap
each other, only the earliest GitHub comment ID remains eligible; later
overlapping candidates lose. Permanently unadmitted Gate B losers are excluded
from every later Gate A election and cannot block a fresh SYN after capacity
opens.

### Gate B — global five-lease capacity

Capacity arbitration is global, not scope-local.

1. For each otherwise-eligible SYN in GitHub comment-ID order, evaluate only the
   immutable valid-history prefix ending at that comment.
2. Count incumbent admitted ASSIGNING plus latest `ASSIGNED` + `WORKING` runs in
   that prefix. If the count is below five, classify the new SYN as permanently
   admitted; otherwise classify it as permanently unadmitted.
3. Persist that classification by derivation from the immutable prefix. Never
   recompute an old losing SYN against a newer, lower incumbent count.
4. Only permanently admitted SYNs consume an ASSIGNING lease and may ACK. A losing
   SYN must not mutate and may append OPEN/backoff; newly freed capacity requires a
   fresh run/SYN.

This makes capacity derivable from durable GitHub order even if losing workers disappear. At four incumbent leases, two simultaneous non-overlapping SYNs can both exist as comments, but only the earlier eligible SYN is an active fifth lease; the other is durably unadmitted rather than creating a persistent sixth lease.

### Phase 2 — 🔵 ASSIGNED / ACK

Only a candidate that passed both gates appends ASSIGNED referencing the ASSIGNING comment ID. Only then may repository mutation begin.

A loser should append 🟢 OPEN for observability, but its unadmitted SYN already has no lease authority. Do not repeatedly compete for the same unchanged scope.

## 5. Branch crash-recovery record

Immediately after a branch is created or selected, and before useful mutation,
append WORKING with the exact branch/head. After each remote branch-head mutation,
append the new head before any further useful mutation, related or unrelated.

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

Use when the fleet should stop trying to self-heal a coordination/topology problem and ask Codex to investigate and repair it. Construct a stable fingerprint and reconstruct complete #140 state first; an unresolved identical fingerprint suppresses another request.

## 7. Codex advisor safety valve

Before concluding there is no work, before repeating unchanged investigation, and before acquiring a sixth active lease, inspect complete latest state plus current branches/PRs/open issues.

Escalate once when any applies:

1. open non-documentation issues remain but there is no safe runnable implementation/rescue/integration work;
2. the same issue/branch/PR/anomaly would be retried without material state/evidence change;
3. active leases already exceed five or a new assignment would exceed five;
4. stale/contradictory/cut-off state, hidden useful work, missing review paths, supersession ambiguity, or branch/LOCK disagreement cannot be reconciled cheaply.

Canonical fingerprint format:

```text
advisor:<repo>:<reason>:<stable-identifiers>:<relevant-head-or-state>
```

Serialize it deterministically:

- `repo` is lowercase `owner/name`;
- `reason` is exactly one of `no-runnable-work`, `repeat-unchanged`,
  `lease-capacity`, or `unreconciled-state`;
- `stable-identifiers` is mechanically fixed to
  `lock-<canonical-coordination-issue-number>` for every reason; for this
  repository it is exactly `lock-140`. Do not add caller-selected issue, PR, ref,
  or run tokens;
- `relevant-head-or-state` is an exact lowercase 40-hex head when one head defines
  the incident. Otherwise it is `sha256-<hex>` over the exact complete set of
  latest valid records for every **non-advisor** new-protocol run plus every active
  legacy record—not a caller-selected subset. Exclude all WAITING ON ADVISOR runs
  and advisor dispatch-candidate/request comments so recovery cannot change its
  own incident hash. Encode included new records as
  `run|seq|state|issue/package|branch|head|pr|author` and legacy records as
  `legacy|comment-id|state|issue/package|branch|head|pr|author`; sort all UTF-8/LF
  lines bytewise ascending and hash them with no trailing newline.

Do not invent aliases, reorder identifiers, or use an unspecified `<state-hash>`.

When five active leases already exist, advisor dispatch uses a **lease-free #140-only control-plane exception**. After complete read-only reconstruction and initial fingerprint deduplication, an otherwise unassigned recovery run may append `🆘 WAITING ON ADVISOR` directly as its first transition (`seq=1`, `prev=none`). It must then reconstruct complete state **again**, collect all unresolved WAITING ON ADVISOR records with the identical fingerprint, and elect the earliest GitHub comment ID as the one durable request. Later identical-fingerprint WAITING records remain non-leases and do not become separate requests. This post-write election closes the concurrent pre-search race.

The earliest WAITING comment is the elected durable request, not a lease owned by
its originating worker. Any later recovery worker may complete dispatch for that
same elected record if the originator is cut off. A recovery worker first appends
a non-mention dispatch-candidate comment containing the fingerprint and elected
WAITING ID, then reconstructs complete history again. Among candidates for that
elected record, the earliest GitHub comment ID wins. Any worker may idempotently
PATCH only that winning candidate comment to the canonical `@codex` request,
elected-record URL, and
`advisor-dispatch:<fingerprint>:<elected-WAITING-comment-id>` marker. Concurrent
helpers PATCH the same object to identical content, so GitHub contains one mention;
losing candidate comments remain inert. On an ambiguous create or PATCH, reread
before retrying.

The exception authorizes only those #140 control-plane comments; branch, PR, issue-body, code, review, merge, or other repository mutation still requires normal admission once capacity is available.

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

A recovery/advisor agent should reconstruct complete state per §3, recompute admitted ASSIGNING prefixes per §4, verify every active WORKING branch/head, correlate PR bases/heads/state, preserve unique useful commits, and repair or escalate contradictions instead of guessing ownership.

## 11. Legacy migration

Do not rewrite historical comments.

- unreleased old `🔒 CLAIM` with an exact branch is legacy 🛠️ WORKING;
- unreleased old `🔒 CLAIM` without an exact branch is legacy 🔵 ASSIGNED;
- old `🔓 RELEASED` is non-active;
- legacy records are part of complete-state reconstruction and any durable-index completeness proof;
- convert legacy state into the new protocol only when that scope is touched or reconciled.

## 12. Examples

### Normal assignment

```text
🟡 ASSIGNING | seq=1 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
base=none | base_sha=none | prev=none |
gate_snapshot=<pre-SYN-sha256> | ts=... | note=account privacy seam
```

```text
🔵 ASSIGNED | seq=2 | run=ux-abc123 | lane=UX | issue/package=#98 |
branch=feature/account-privacy | head=<main-sha> | pr=none |
base=none | base_sha=none | prev=ASSIGNING/<comment-id> | ack=<comment-id> |
eligibility=clear | gate_snapshot=<post-SYN-sha256> |
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
fingerprint=advisor:geromet/tornhappygymstats:lease-capacity:lock-140:sha256-<canonical-64-hex> |
note=lease-free saturated-control-plane candidate; earliest identical-fingerprint WAITING is elected and its earliest dispatch-candidate comment is idempotently patched to the @codex request
```

The goal is not ceremony. The goal is that an interrupted worker leaves enough durable, ordered evidence for the next worker or Codex advisor to recover safely without inventing state.
