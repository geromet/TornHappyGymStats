# HappyGymStats working agreement

This is the cross-agent source of truth for repository workflow and safety rules.
A clean clone must contain everything needed to work safely. Gitignored
`workspace/` material may explain history or hold local evidence, but it is never
an authority for correctness, acceptance criteria, or implementation decisions.

Read [`AGENT-COORDINATION-PROTOCOL.md`](AGENT-COORDINATION-PROTOCOL.md) for the
wire-level state/handshake format used in GitHub issue #140.

## 1. Planning and live coordination state live in GitHub

GitHub issues are the authoritative backlog. **Issue #140 is the canonical live
Agent coordination/state log for this repository.** Current repository, PR, CI,
branch state, and the latest valid protocol transitions win over stale issue
prose, old PR descriptions, audit notes, plans, historical handoffs, or an agent's
memory of a prior run.

Active ownership has no TTL. Before selecting mutable work or counting capacity,
paginate **all** #140 comments and recognize both new-protocol transitions and
legacy CLAIM/RELEASE records, or use a mechanically maintained durable index proven
equivalent for **all recognized protocol and legacy records**. Validate each new-
protocol run's sequence/prev/ACK chain and reconstruct every run's latest valid
state. A recent-comment window is never authoritative for ownership or lease
counting.

Before materially conflicting mutation, refresh:

1. complete reconstructed #140 protocol + legacy state and the current issue body;
2. the target issue/PR and relevant dependencies;
3. the repository's actual current default branch and head;
4. every branch/PR head involved in the mutation.

Read-only inspection does not require ownership. Mutable work uses the append-only
state machine below.

### 🚦 Assignment and work states

- 🟢 **OPEN** — default/unowned; no active lease.
- 🟡 **ASSIGNING** — phase-1 assignment request / SYN; an active lease only when
  admitted by the deterministic capacity election.
- 🔵 **ASSIGNED** — phase-2 acknowledged ownership / ACK.
- 🛠️ **WORKING @ branch** — active mutation at an exact branch/head.
- 🧪 **WAITING ON PR CR** — coherent package waiting on Codex code/final review.
- 🆘 **WAITING ON ADVISOR** — Codex must investigate/repair coordination or
  branch/PR topology.
- ✅ **FINISHED** — issue/package is actually completed.

Before mutation, append ASSIGNING with a unique `run=`, `seq=1`, exact
issue/package/seam, intended branch and observed heads. Then reconstruct complete
#140 state again. If any materially overlapping run is already ASSIGNED or
WORKING, the newcomer must back off; a new ASSIGNING record never supersedes an
active owner. Among overlapping ASSIGNING requests, the earlier GitHub comment ID
wins.

Capacity admission is global, deterministic, and cleanup-independent. Count
incumbent ASSIGNED/WORKING leases, compute remaining slots up to five, then sort
every otherwise-eligible latest ASSIGNING record by GitHub comment ID ascending.
Only the earliest records that fit those slots are **admitted ASSIGNING leases**
and may ACK. Every later SYN is durably unadmitted, consumes no lease, and must not
mutate; it should append OPEN/backoff when able, but correctness does not depend on
that cleanup. Thus concurrent pre-admission comments cannot leave a persistent
sixth active lease if a losing worker is cut off.

A winning candidate appends a **new** ASSIGNED record with `seq=2`, repeats the
exact scope/branch/expected heads, and references the ASSIGNING comment ID as
`ack=`. Mutation is forbidden until this acknowledgement exists.

Every later transition is another **new #140 comment** with monotonic sequence,
exact branch/head/PR, previous state/comment, timestamp and short result. PR-facing
WAITING/proof transitions additionally record machine-readable `base=<ref>` and
`base_sha=<sha>` for the exact base the evidence covered. Do not edit an old state
comment into a new state. Before retrying an ambiguous comment write, reread and
suppress an already-present same `run+seq` record.

Immediately after a branch is selected/created and before useful mutation, append
🛠️ WORKING with the exact branch/head. After every remote branch-head change,
append another WORKING record with the new head. The recorded head is a CAS token:
unexpected movement stops mutation until reconciled. Never force-push through a
race or commandeer/rewrite an outside-contributor or human-owned branch.

Only admitted ASSIGNING, ASSIGNED and WORKING states consume the five active fleet
leases. Unadmitted SYN, OPEN, WAITING ON PR CR, WAITING ON ADVISOR and FINISHED do
not. Do not silently reclaim an admitted active state just because a worker may
have been cut off.

The wire format, global admission ordering, complete reconstruction algorithm and
legacy `🔒 CLAIM` migration rules are defined in
`docs/AGENT-COORDINATION-PROTOCOL.md`.

### 🆘 Codex advisor safety valve

Before concluding there is no work, before repeating unchanged investigation, and
before acquiring a sixth active lease, reconstruct complete #140 state per run and
inspect live branches, PRs and open issues.

Transition once to WAITING ON ADVISOR and send one deduplicated `@codex` request
when any of these applies:

1. open **non-documentation** issues remain but no safe runnable implementation,
   rescue or integration work can be found;
2. the same issue/branch/PR/anomaly would be attempted again without material
   head/state/evidence change;
3. active fleet leases exceed or would exceed five;
4. stale/contradictory/cut-off state, hidden useful work, a missing PR/review path,
   supersession ambiguity, or branch/LOCK disagreement cannot be cheaply and
   confidently reconciled.

At five already-active leases, advisor dispatch uses an explicit **lease-free
control-plane exception**. After complete read-only state reconstruction and an
initial stable-fingerprint deduplication, an otherwise unassigned recovery run may
append WAITING ON ADVISOR directly as its first `seq=1`, `prev=none` transition.
It must then reconstruct complete state again, compare every unresolved WAITING
record with the identical fingerprint, and allow only the earliest GitHub comment
ID to post the matching `@codex` request. Later identical-fingerprint candidates
stay non-leases and do not dispatch. This post-WAITING election closes the race
between simultaneous pre-write fingerprint searches.

The exception authorizes only those #140 control-plane comments; branch, PR,
issue-body, code, review, merge, or any other repository mutation still requires
the normal ASSIGNING → ASSIGNED handshake after capacity is available.

Use the stable advisor fingerprint format from the protocol doc and reconstruct
complete #140 state before dispatch. Codex is asked to inspect **and repair** live
state/topology/work, preserve useful commits, use the same state protocol for
mutations, and report concrete results or blockers.

### 📦 Review-unit economy and branch-explosion prevention

Reviewer cost is a real resource. For Torn, **three open fleet-owned
default-destined PRs is the normal operating cap; five is the emergency ceiling**.
At or above three, do not create another default-destined PR except an urgent,
independently reviewable stop-line/default-regression/security repair.

Prefer persistent feature/work-package/integration branches and existing
draft/open PRs across multiple runs. A new default-destined PR should normally
represent a complete meaningful feature/work-package, multiple tightly related
acceptance slices/issues, or a coherent integration rollup—not an hourly micro-
fragment. Parallel child branches are for useful isolation only and should feed a
claimed non-default integration branch when compatible.

Normally send at most one new Codex final-review/merge handoff per repository per
hour, preferably less. A changed head while a package is still accumulating is not
itself a reason to re-handoff.

### Default-branch authority boundary

Fleet/manual agents **must never merge a branch or PR into the repository default
branch** or weaken protections to permit such a merge. Final default-branch review
and merge belong exclusively to Gerome's designated Codex/human workflow.

Codex final review/merge must still refresh complete #140 protocol + legacy state,
current default, candidate PR exact head/base SHA, checks, reviews, dependencies
and ownership before each merge decision. After a default merge, refresh default
and re-evaluate remaining PRs; do not inherit readiness from pre-merge state.

## 2. Torn is read-only from HappyGymStats

No code path may perform a state-changing Torn action. Do not automate attacks,
refills, travel, item/money movement, scripted clicks, or any other game action.
Recommendations and normal Torn links that a human deliberately clicks are fine.
Torn API integrations in this repository are observation/intelligence only.

This is a standing product boundary, not a temporary implementation preference.
Issue #104 owns stop-and-report gates for features whose premise still needs
measurement or API feasibility proof.

## 3. Stored Torn keys have two hard boundaries

`Ecies` is not the server-side war-key vault. It encrypts for a client-held key
and therefore cannot satisfy unattended server decryption. Server-stored member
keys use the existing `WarKeyVault` envelope-encryption design rooted in
`WAR_KEY_MASTER`; extend `scripts/verify/w07-key-vault-contract.sh` rather than
inventing a second credential scheme.

A member Torn key must not be persisted before versioned consent for that member
and purpose is recorded. `/terms` and `docs/torn-api/terms-of-service.md` carry
the disclosure/version; issue #80 owns the consent + stored-key transaction gate,
link/replace/revoke flow, and private telemetry lifecycle. Never log, redisplay,
or return a submitted Torn key.

## 4. Proof must match where the change can fail

Local agent verification is **change-directed**, not an automatic reproduction of
the complete GitHub CI matrix. Start with the smallest deterministic proof that
can meaningfully falsify the affected behavior: a focused test, relevant verifier,
affected-project build, render check, integration fixture, or other proof at the
actual failure boundary.

Do **not** automatically run every repository test, every verifier, PostgreSQL,
browser proof, formatting, or the complete canonical gate merely because a task
changed code. Broaden local verification when:

- the changed seam has wide or uncertain impact;
- the issue/acceptance criteria explicitly require broader or higher-tier proof;
- focused proof fails or exposes adjacent uncertainty;
- verifier, test-routing, build, or CI infrastructure itself changed; or
- current-head GitHub CI reports a deterministic failure that needs local
  reproduction and repair.

For **default-targeting PR heads that actually receive the repository's broad CI
workflow**, GitHub Actions may supply the broad regression pass. Before declaring
work ready, inspect required checks on the exact current head and repair
deterministic failures.

Do **not** assume every PR head receives that workflow. Child PRs targeting
non-default feature/stable/integration branches may not trigger broad CI. When an
exact head has no broad CI coverage, run the complete local source/build gate
before declaring that child review-ready unless its acceptance criteria require a
stronger evidence tier:

```bash
bash scripts/verify/build-and-test.sh
```

Pending, unavailable, failed, or old-head checks are not proof. Never describe a
command or evidence tier as observed unless it actually ran.

Verifier routing is owned by `scripts/verify/manifest.tsv`; do not add a second
handwritten verifier list. A new verifier must be registered there, and an
excluded verifier needs a concrete reason. Missing verifier dependencies are an
unavailable proof and must fail closed when that verifier is required.

Evidence tiers are about the environment capable of falsifying the change:

- **T1 — source/contracts/tests:** focused deterministic source/build/test proof
  appropriate to the changed behavior, with a regression or negative control
  where practical. Use the complete canonical gate when the change has broad
  impact, the work package explicitly requires it, or the exact non-default child
  head has no broad CI coverage.
- **T2 — rendered UI:** T1 plus actual rendering/browser evidence. Use
  `scripts/screenshot-board.sh` where applicable and inspect the relevant 390,
  768, and desktop output; source inspection alone is not UI proof.
- **T3 — deploy/remote/operator:** offline tests/lints first, then an explicit
  operator handoff for the environment-specific dry run. Agents do not turn a
  missing SSH/passkey/TTY into a weaker claim of completion.
- **T4 — PostgreSQL/relational:** real PostgreSQL execution with
  `HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1`. A skipped relational test is
  not proof. Issue #60 owns the dedicated CI job and its non-zero/zero-skip
  contract.

Issue #61 owns mechanical changed-path classification; #77 owns the compact PR
record of required versus observed evidence. Until those checks are merged,
record the same facts explicitly in the PR body and never describe pending proof
as observed.

Formatting is `dotnet format whitespace --verify-no-changes`, never bare
`dotnet format`: bare format also applies analyzer fixes and has changed SQL
semantics in this repository before.

## 5. Remote and production actions belong to the human operator

`scripts/deploy*.sh`, `scripts/recon-*.sh`, `scripts/setup-*.sh`, applying paths
through `scripts/menu.sh`, and any other production/SSH mutation are operator
steps. They need interactive Cloudflare/SSH credentials and often a TTY. Agents
may improve and offline-test these scripts, but must not claim the environment
step ran when it did not.

Read `docs/OPERATIONS-PITFALLS.md` before modifying remote execution. Keep
scripts dry-run by default and preserve their explicit `DEPLOY_*=1` plus
`--confirm-*` mutation gates. `scripts/verify/remote-heredoc-lint.sh` protects
known shell/SSH expansion failures; issue #64 owns the disposable real SSH/PTY
fixture.

## 6. Keep architecture simpler than the problem

Prefer existing capability boundaries and framework primitives over new wrapper
layers. Do not introduce a generic repository, MediatR/CQRS ceremony, an
interface per helper, or catch-all `Common`, `Shared`, or `Abstractions` projects
without a real external/runtime boundary. Refactors should reduce the concepts a
cold reader needs, not just move them elsewhere. Issues #71, #72, #73, #74,
#110, and #111 own the current simplification work.

The separate AdminPanel process is an intentional least-privilege/read-only
boundary by default. Do not merge it into the member Blazor host merely to lower
project count.

## 7. Preserve truthful data semantics

Measured, projected, inferred, stale, and unknown are not interchangeable.
Rendered war figures use the existing `Figure`/`FigureKind` vocabulary. Never
backdate a current snapshot into historical facts, turn missing samples into
zero, flatten provenance away, or present a model/counterfactual as observed or
causal fact.

Private member/faction data is authorized and filtered server-side. Do not trust
a client-supplied faction/role/scope boundary.

## 8. `workspace/` is supporting material only

`workspace/` is gitignored. It can hold screenshots, reports, local handoff
notes, or historical archives, but a clean clone must not need it. If a fact is
load-bearing for safe implementation, move that fact into a tracked issue,
document, test, verifier, contract, or code comment before relying on it.

Do not cite `workspace/V2`, `workspace/handoff`, or archived GSD state as the sole
source of an acceptance criterion. Historical material may explain why a rule
exists; tracked enforcement is what makes the rule current.

## 9. Handoff and terminal-state standard

Before handing a PR to Codex, state:

- which issue/scope it implements and what it deliberately does not;
- dependencies/stacking and the exact base ref/SHA and head SHA it was proved against;
- required evidence tier(s);
- commands/evidence actually observed on the exact final head;
- regression/negative control;
- anything still unverified or requiring human/operator action.

Use `Closes #N` only when the PR satisfies the issue's full current acceptance
criteria. Partial work uses `Refs #N` and leaves the issue open.

When a coherent package is ready for Codex review, append **🧪 WAITING ON PR CR**
with exact PR/head plus machine-readable base ref/SHA/evidence and then send one
deduplicated same-head Codex handoff. Waiting on review does not consume a fleet
implementation lease.

Use **✅ FINISHED** only after the issue/package is truly complete and required
default incorporation or explicit completed/not-planned disposition is verified.
A pushed branch, open PR, green CI run, or child merge into non-default is not by
itself FINISHED.

Useful work that is not finished must remain discoverable through the protocol:
exact branch/head in WORKING, an explicit WAITING state, a current review surface,
proven incorporation/supersession, or explicit OPEN/abandonment with unique-commit
assessment. Never rely on remote branch existence alone as the ownership record.

When tracing branch history, do not use `ahead` alone as evidence of missing work.
Account for squash merges, stable rollups, replacement PRs, and explicit
supersession. Remote deletion belongs only to dedicated cleanup/provenance work,
not ordinary implementation cleanup.
