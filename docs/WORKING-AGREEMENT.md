# HappyGymStats working agreement

This is the cross-agent source of truth for repository workflow and safety rules.
A clean clone must contain everything needed to work safely. Gitignored
`workspace/` material may explain history or hold local evidence, but it is never
an authority for correctness, acceptance criteria, or implementation decisions.

## 1. Planning and live coordination state live in GitHub

GitHub issues are the authoritative backlog. **Issue #140 is the canonical live
Agent work coordination LOCK for this repository.** Current repository, PR, CI,
and branch state wins over stale issue prose, old PR descriptions, audit notes,
plans, or historical handoff comments.

Before selecting mutable work, read #140's current body and recent comments. Before
any materially conflicting mutation, refresh:

1. #140 body + recent comments;
2. the target issue/PR and relevant dependencies;
3. the repository's actual current default branch and head (discover it; do not
   rely on an old assumption that it is still `main`);
4. every branch/PR head involved in the mutation.

Obey all live coordination restrictions recorded in #140: active claims, WIP and
throughput gates, queue/drain ordering, branch ownership, dependency restrictions,
and outside-contributor boundaries. A free-looking issue is not available when a
live gate or overlapping ownership rule says otherwise.

Read-only inspection does not require a claim. Before editing/pushing code or a
fleet branch, changing a PR body/base/state, posting a substantive PR review or
comment, updating an overlapping implementation issue, or consolidating/closing
fleet work, acquire ownership with the two-phase claim protocol:

1. **Pre-claim:** immediately refresh #140, the target, default, and relevant
   heads; reject the candidate if a materially overlapping active claim exists.
2. **Claim:** record the exact issue/PR/branch/work-package/seam, a unique
   `run=<token>`, and the observed head SHA where practical.
3. **Post-claim win check:** reread recent #140 comments immediately. If claims
   overlap, the earlier GitHub comment ID wins. The later claimant must edit its
   own claim to canonical `🔓 RELEASED` and choose independent work.
4. **Before first mutation:** refresh #140 and the relevant heads once more.

After two failed acquisitions for overlapping work, back off to lower-priority
independent work rather than repeatedly competing for the same scope.

Treat an observed branch head SHA as a compare-and-swap token. Before every remote
branch mutation, verify the current head still matches the expected head. If it
moved unexpectedly, stop and reconcile before writing. Never force-push through a
race or commandeer/rewrite an outside-contributor or human-owned branch.

For ordinary task flow:

1. read the issue and its current comments;
2. check open and recently merged PRs for overlapping work;
3. refresh from the actual current default before starting a new independent task;
4. keep one active task on one branch and name stacked/dependent PRs explicitly;
5. after a PR merges or closes, follow-on work gets a fresh branch unless the
   original task is explicitly reopened.

Coordination epics are not implementation tasks. Respect dependency and stop-gate
ordering recorded in the child issues. `docs/MILESTONES.md` and
`docs/UX-PLAN.md` are pointers, not parallel planning databases.

### Default-branch authority boundary

Fleet/manual agents **must never merge a branch or PR into the repository default
branch** or weaken protections to permit such a merge. Final default-branch review
and merge belong exclusively to Gerome's separately invoked coding-agent/human
workflow. Fleet/manual agents may build, repair, test, review, and consolidate
compatible fleet-owned work only through non-default branches and PRs.

A final coding-agent session that Gerome explicitly authorizes to review/merge
must still refresh #140, the current default head, the candidate PR exact head,
checks, reviews, dependencies, and any live ownership before each merge decision.
After a default merge, refresh the default branch and re-evaluate remaining PRs;
do not inherit readiness from their pre-merge base state.

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

The canonical source/build gate is:

```bash
bash scripts/verify/build-and-test.sh
```

Verifier routing is owned by `scripts/verify/manifest.tsv`; do not add a second
handwritten verifier list. A new verifier must be registered there, and an
excluded verifier needs a concrete reason. Missing verifier dependencies are an
unavailable proof and must fail closed.

Evidence tiers are about the environment capable of falsifying the change:

- **T1 — source/contracts/tests:** canonical gate plus a regression or negative
  control that fails without the change.
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
notes, or historical archives, but a cold clone must not need it. If a fact is
load-bearing for safe implementation, move that fact into a tracked issue,
document, test, verifier, contract, or code comment before relying on it.

Do not cite `workspace/V2`, `workspace/handoff`, or archived GSD state as the sole
source of an acceptance criterion. Historical material may explain why a rule
exists; tracked enforcement is what makes the rule current.

## 9. Handoff standard

Before handing a PR back, state:

- which issue/scope it implements and what it deliberately does not;
- dependencies/stacking and the exact base/head state it was proved against;
- required evidence tier(s);
- commands/evidence actually observed on the exact final head;
- regression/negative control;
- anything still unverified or requiring human/operator action.

Use `Closes #N` only when the PR satisfies the issue's full current acceptance
criteria. Partial work uses `Refs #N` and leaves the issue open.

### Terminal handoff invariant

A branch being pushed, or a child PR being merged into a non-default
stable/integration branch, is **not** enough to declare useful work finished.
Before releasing ownership, every useful branch touched or created by the run must
be in exactly one recorded terminal state:

1. directly or transitively represented by an **open PR ultimately targeting the
   repository default branch**;
2. proven incorporated or superseded by a current default-destined review surface,
   with that relationship recorded; or
3. explicitly abandoned after its unique commits were assessed and the reason was
   recorded.

When tracing branch history, do not use `ahead` alone as evidence of missing work.
Account for squash merges, stable rollups, replacement PRs, and explicit
supersession. If a non-default stable branch becomes a coherent review unit and
still contains useful work absent from default, open or update a live
stable-to-default review surface for Gerome/coding-agent review. Do not close the
only default-destined visibility surface merely to reduce PR count.

Immediately before releasing a claim, refresh #140 and the relevant branch/PR
heads one final time. Edit the **same claim comment** to canonical `🔓 RELEASED`
and record the durable terminal disposition, exact final head/evidence where
relevant, and any truthful remaining gap.
