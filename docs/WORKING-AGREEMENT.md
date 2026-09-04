# HappyGymStats working agreement

This is the cross-agent source of truth for repository workflow and safety rules.
A clean clone must contain everything needed to work safely. Gitignored
`workspace/` material may explain history or hold local evidence, but it is never
an authority for correctness, acceptance criteria, or implementation decisions.

## 1. Planning state lives in GitHub

GitHub issues are the authoritative backlog. Before changing code:

1. read the issue and its current comments;
2. check open and recently merged PRs for overlapping work;
3. refresh from current `main` before starting a new independent task;
4. keep one active task on one branch and name stacked/dependent PRs explicitly;
5. after a PR merges or closes, follow-on work gets a fresh branch unless the
   original task is explicitly reopened.

Coordination epics are not implementation tasks. Respect dependency and stop-gate
ordering recorded in the child issues. `docs/MILESTONES.md` and
`docs/UX-PLAN.md` are pointers, not parallel planning databases.

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
- dependencies/stacking and the base it was built from;
- required evidence tier(s);
- commands/evidence actually observed;
- regression/negative control;
- anything still unverified or requiring human/operator action.

Use `Closes #N` only when the PR satisfies the issue's full current acceptance
criteria. Partial work uses `Refs #N` and leaves the issue open.
