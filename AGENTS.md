# HappyGymStats agent entrypoint

Read [`docs/WORKING-AGREEMENT.md`](docs/WORKING-AGREEMENT.md) before changing the
repository. It is the cross-agent source of truth for safety, live task ownership,
evidence tiers, remote/operator boundaries, data provenance, and handoff rules.

Read [`docs/AGENT-COORDINATION-PROTOCOL.md`](docs/AGENT-COORDINATION-PROTOCOL.md)
for the machine-readable coordination/state protocol. GitHub issue **#140** is the
canonical live state log.

## 🚦 Live coordination before mutation

Current repository/PR/issue state and the latest valid #140 protocol transitions
win over stale issue prose, old PR descriptions, historical handoffs, or an
agent's memory of a previous run.

The coordination states are:

- 🟢 **OPEN** — default/unowned;
- 🟡 **ASSIGNING** — phase-1 assignment request / SYN;
- 🔵 **ASSIGNED** — phase-2 acknowledged ownership / ACK;
- 🛠️ **WORKING @ branch** — active mutation at an exact branch/head;
- 🧪 **WAITING ON PR CR** — coherent package waiting on Codex code/final review;
- 🆘 **WAITING ON ADVISOR** — Codex must investigate/repair coordination or
  branch/PR topology;
- ✅ **FINISHED** — issue/package is actually completed.

Active ownership has no TTL. Before admitting mutable work, paginate **all** #140
protocol comments (or use a mechanically complete durable index proven equivalent),
validate each run's sequence/prev/ACK chain, and reconstruct every run's latest
valid state. A recent-comment window is never authoritative for ownership or lease
counting.

Before mutation, use the append-only ASSIGNING → ASSIGNED handshake in #140. A
run is not allowed to mutate merely because it posted ASSIGNING. After posting,
reconstruct complete state again. If any materially overlapping run is already
ASSIGNED or WORKING, the newcomer must back off. Among overlapping ASSIGNING
requests, the earlier GitHub comment ID wins.

Admission is also globally capacity-ordered. Count incumbent ASSIGNED/WORKING
leases, compute the remaining slots up to five, then sort every otherwise-eligible
latest ASSIGNING record by GitHub comment ID ascending. Only the earliest records
that fit those slots may ACK; every later candidate appends OPEN/backoff. This
prevents non-overlapping claims from racing the fleet above five active leases.

Every state transition is a **new** #140 comment with a unique `run=`, monotonic
`seq=`, exact issue/package, branch, head, PR, previous state/comment, timestamp,
and short result. PR-facing WAITING/proof records also carry machine-readable
`base=<ref>` and `base_sha=<sha>`. Do not edit an old state comment into a new
state. Ambiguous writes are retried only after rereading and suppressing an already-
present same `run+seq` record.

Immediately after a branch is selected/created and before useful mutation, append
🛠️ WORKING with the exact branch/head. After every remote head change, append
another WORKING transition with the new head. Treat the observed head as a CAS
token: unexpected movement stops mutation until reconciled. Never force-push
through a race.

Only ASSIGNING, ASSIGNED, and WORKING consume the five active fleet leases.
Waiting/open/finished states do not. Do not silently reclaim stale active state
just because an automation may have been cut off.

### 🆘 Codex advisor safety valve

Before concluding there is no work, before repeating unchanged investigation, and
before acquiring a sixth active lease, reconstruct complete #140 state per run and
inspect current branches/PRs/open issues.

Use 🆘 WAITING ON ADVISOR plus one deduplicated `@codex` request when any of these
is true:

1. open **non-documentation** issues remain but no safe runnable implementation,
   rescue, or integration work can be found;
2. the same issue/branch/PR/anomaly would be attempted again without material
   head/state/evidence change;
3. active leases exceed or would exceed five;
4. stale/contradictory/cut-off state, hidden useful work, a missing PR path,
   supersession ambiguity, or branch/LOCK disagreement cannot be cheaply and
   confidently reconciled.

At five already-active leases, advisor dispatch is an explicit **lease-free
control-plane exception**: after complete read-only reconstruction and fingerprint
deduplication, an unassigned recovery run may append WAITING ON ADVISOR directly
as `seq=1`/`prev=none` and post the single matching `@codex` request without first
creating ASSIGNING/ASSIGNED. That exception permits only the #140 advisor control-
plane comments; branch, PR, issue-body, code, review, merge, or other repository
mutation still requires the normal handshake once capacity is available.

Fingerprint and deduplicate advisor requests as documented in
`docs/AGENT-COORDINATION-PROTOCOL.md`. Codex is asked to investigate **and repair**
live state/topology/work, not merely write another status report.

## 📦 Review-unit economy

Reviewer cost is a real resource. Prefer fewer, larger, coherent review units over
hourly micro-PRs.

For Torn, **three open fleet-owned default-destined PRs is the normal operating
cap; five is the emergency ceiling**. At or above three, do not create another
default-destined PR except an urgent independently reviewable stop-line,
default-regression, or security repair.

Prefer persistent feature/work-package/integration branches and existing
draft/open PRs across multiple runs. A new default-destined PR should normally be
a complete meaningful feature/work-package, multiple tightly related acceptance
slices/issues, or a coherent integration rollup. Parallel child branches are only
for useful isolation and should feed a claimed non-default integration branch when
compatible.

Normally make at most one new Codex final-review/merge handoff per repository per
hour, preferably less. A changed head on an accumulating package is not by itself
a reason to re-handoff.

## Default-branch authority and durable handoff

Fleet/manual agents **must never merge into `main` or any repository default
branch**. Final default-branch review and merge belong to Gerome's designated
Codex/human workflow.

🧪 WAITING ON PR CR is the normal state after a coherent package is ready for
Codex review. Its record must include exact PR head plus exact base ref/SHA so
recovery can tell which base the evidence covered. ✅ FINISHED is reserved for
actual issue/package completion after required default incorporation or an explicit
completed/not-planned terminal disposition is verified.

A child merged only into a non-default integration branch is not default
incorporation. Preserve a current review path for useful work; do not hide it merely
to reduce PR count.

## Verification economy

Use **change-directed local verification**. Start with the smallest deterministic
proof that can meaningfully falsify the affected behavior; broaden when risk,
acceptance criteria, focused-proof failure, verifier/CI changes, or deterministic
CI failures require it.

For **default-targeting PR heads that actually receive the repository's broad CI
workflow**, GitHub Actions may supply the broad regression pass. Inspect required
checks on the exact current head and fix deterministic failures.

Do **not** assume every PR head receives broad CI. Child PRs targeting non-default
feature/stable/integration branches may not trigger the broad workflow. When the
exact head has no broad CI coverage, run the canonical local source/build gate
before declaring the child review-ready unless the work package explicitly
requires an even stronger proof tier:

```bash
bash scripts/verify/build-and-test.sh
```

Pending, unavailable, failed, or old-head checks are not proof. Never claim a
command or evidence tier was observed unless it actually ran. See
`docs/WORKING-AGREEMENT.md` §4.

Then use:

- [`README.md`](README.md) for the repository map;
- [`docs/OVERVIEW.md`](docs/OVERVIEW.md) for architecture;
- GitHub issues for authoritative planned work and dependency/stop-gate state;
- `scripts/verify/manifest.tsv` for the canonical verifier graph;
- `docs/OPERATIONS-PITFALLS.md` before touching deploy/SSH/remote-exec code.

Do not treat gitignored `workspace/` material as required project state. A clean
clone must be enough to work safely. Do not run production/deploy/remote mutation
steps on the user's behalf; record them as T3 operator handoff when required.
