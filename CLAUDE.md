# HappyGymStats — working agreement

Torn telemetry: ASP.NET Core API + Blazor frontend + AdminPanel, Postgres,
Keycloak auth. .NET 10 (`global.json` pins SDK 10.0.106). Nine projects under
`src/`, tests in `tests/HappyGymStats.Tests`.

Read `README.md` for the map and `docs/OVERVIEW.md` for the architecture. This
file carries only what cannot be rediscovered from the code — the rules whose
violation is expensive and silent.

## Two standing non-goals

These are not preferences. They come from `docs/MILESTONES.md` and the hand-off
pack, and they hold for every milestone.

1. **No game actions, ever.** No code path may issue a state-changing request to
   Torn — no auto-attack, refill, travel, or scripted click. Links to Torn are
   plain anchors a human clicks. Reads are fine; writes are not.
2. **`Ecies` must not be reused for the war key vault.** It encrypts to a
   client-held public key, so the server *cannot* decrypt — useless for a key the
   server must use unattended. The vault uses envelope encryption keyed off
   `WAR_KEY_MASTER`.

A third, from M009: **a Torn API key row must not be persisted before that member's
consent is recorded.** The disclosure half is done — `docs/torn-api/terms-of-service.md`
is published (v2.0.0) and served at `/terms`. The consent half is not:
`ConsentRecordEntity` (S01) does not exist yet. `scripts/verify/w07-key-vault-contract.sh`
binds this to a moment rather than a word — it turns into a hard failure as soon as
any non-test source both names `StoredApiKey` and calls `SaveChanges`. That is a live
Torn ToS obligation, not a nicety.

## Anything that touches the server is the user's to run

`scripts/deploy*.sh`, `scripts/recon-*.sh`, `scripts/setup-*.sh` and
`scripts/menu.sh` need a Cloudflare Access passkey and an SSH key passphrase, and
they prompt interactively. **Do not run them.** Hand the command to the user to
run in their own terminal with the `! ` prefix:

    ! bash scripts/menu.sh

Every one of those scripts is **dry-run by default**. Applying needs both its
`DEPLOY_*=1` environment gate and its `--confirm-*` flag. `menu.sh` supplies
those arguments as a convenience — it is not a bypass, and neither are you.

Read `docs/OPERATIONS-PITFALLS.md` before proposing any change to the remote-exec
scripts. Most entries cost a real evening. In particular: inside an unquoted
remote heredoc, escape every `$`, `` ` `` and `$( )` that must reach the server —
`bash scripts/verify/remote-heredoc-lint.sh` guards this offline.

## Verification is executable, not prose

Before handing work back, run:

    bash scripts/verify/build-and-test.sh

It runs the offline contract checks *and* `dotnet build` + `dotnet test`. Slices
own a script in `scripts/verify/`; `menu-contract.sh` fails if a script exists
with no operator-console entry, so a new script means a new registry row or a
`REG_EXCLUDED` reason in `scripts/lib/registry.sh`.

Never claim a check passed without having run it and read the output.

**A green suite here does not mean the Postgres tier ran.**
`PostgresApiIntegrationTests` reports *passed* when it skips, and it skips with
no Docker daemon — this machine has rootless podman only. CI sets
`HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1`, which turns a skip into a hard
failure; that is where a Postgres change is actually verified. See the
`fixing-a-bug` skill for which evidence each kind of bug needs.

Formatting is checked with `dotnet format whitespace`. Never bare `dotnet
format` — it also applies analyzer code fixes, and once rewrote
`ExecuteSqlRawAsync` to `ExecuteSqlAsync`, changing the SQL a test generates.

## Where the plans live, and which copy is real

- `docs/MILESTONES.md` — feature milestones `M007`–`M013`.
- `docs/UX-PLAN.md` — UX slices `U001`–`U006`.
- `workspace/V2/` — the authoritative hand-off pack. **Cite `workspace/V2/...`
  paths only.** `workspace/handoff/` (no `V2`) is a stale subset missing docs
  05–11.
- `workspace/` is gitignored working material. Do not commit it, and do not treat
  `workspace/tmp/` output (screenshots) as repo content.
- `workspace/archive/GSD/STATE.md` and `ROADMAP.md` belong to an external tool.
  `STATE.md` is **deliberately stale**. Do not edit or "fix" either.

Some milestones have **gates** — stop-and-report points, not tasks. `docs/MILESTONES.md`
lists them in a table. A gate's outcome gets written down whether or not it is
favourable; a documented dead end is a complete outcome.

## Voice

Commits and docs here explain *why*, in full sentences, and name what the change
cost or prevented — `feat(ux): screenshot the board instead of reasoning about it`,
not `feat: add screenshot script`. Match it. Do not compress this repo's prose
into note form.
