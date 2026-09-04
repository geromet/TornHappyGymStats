---
name: fixing-a-bug
description: Use when something in HappyGymStats is wrong, broken, failing, or behaving unexpectedly — a failing test, a bad figure on the war board, a deploy or remote script that misbehaves, a Postgres or migration fault — and before claiming any such fix is done.
---

# Fixing a bug

## Overview

**REQUIRED BACKGROUND:** Use `superpowers:systematic-debugging` for the method —
reproduce, isolate, find the root cause before proposing a fix. This skill does
not repeat it.

What this adds is the part that is specific to this repository: **how far you
can prove a fix, and what counts as proof at each level.** Four tiers. Two of
them you cannot verify on this machine, and knowing that up front is the point —
the expensive mistake here is not a wrong fix, it is a fix declared done on
evidence that could never have shown the defect.

## The four tiers

Pick by *where the bug lives*, not by how hard it looks.

| Tier | The bug is in | Proof that it is fixed |
|---|---|---|
| 1 | Logic, contracts, DTOs, anything with a unit test | `bash scripts/verify/build-and-test.sh` green, plus a check that fails without your fix |
| 2 | Anything rendered — Razor, layout, a figure, a caption | Tier 1 **and** a screenshot you have opened and looked at |
| 3 | Deploy, recon, or any remote script | Offline lints, `docs/OPERATIONS-PITFALLS.md`, then the operator runs the dry run |
| 4 | Postgres, migrations, the integration tier | CI, with `HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1` |

### Tier 1 — logic

Write the check that fails first. A fix with no failing check is a claim.
`superpowers:writing-a-verify-script` covers a slice contract;
`tests/HappyGymStats.Tests` covers behaviour. Then run the full gate — the
offline contract checks catch regressions in things you were not looking at.

### Tier 2 — anything visible

**A rendered bug cannot be fixed by reading Razor.** U001 shipped a
`"(inferred) inferred"` caption and an operator diagnostic inside a user-facing
error banner; both were invisible in the source and obvious in the first frame.

Use `looking-at-the-app`. Shoot it, open the images, look. The screenshot you
did not open proves nothing.

### Tier 3 — remote and deploy scripts

**You cannot run these.** They need the operator's Cloudflare Access passkey and
SSH passphrase, and they prompt interactively. What you can do:

1. `bash scripts/verify/remote-heredoc-lint.sh` — offline, no host. Catches the
   whole class of bug where an unquoted outer heredoc expands the inner one
   locally.
2. `bash -n <script>` for syntax, and read `docs/OPERATIONS-PITFALLS.md` — every
   entry there cost a real evening, and the symptom you are looking at is
   probably listed with a cause that is *not* what it looks like.
3. Hand the dry run to the operator: `! bash scripts/menu.sh`. Every script is
   dry-run by default; applying needs its `DEPLOY_*=1` gate and `--confirm-*`
   flag, and those are theirs to give.

The governing rule from the pitfalls file: **the error message points at the
last thing that failed, not the thing that broke.** A missing `rsync`, a
"missing" Postgres role, a 404 on a cached file and a crash-looping frontend
were all misdirection.

### Tier 4 — Postgres and migrations

**A green local suite is not evidence here.** `PostgresApiIntegrationTests`
reports *passed* when it skips, and it skips whenever there is no Docker daemon
— which is the case on this machine, where only rootless podman is installed.
Three of those tests were green and vacuous for months.

So: a Postgres fix is verified by CI, which sets
`HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1` and turns a skip into a hard
failure naming the reason. Push the branch and read the run. Locally you can
only prove you did not break anything else.

To check what a local run actually did rather than assuming:

    dotnet test --filter "Category=PostgresApiIntegration" -v n

A `[skip]` or `[docker]` line in the output means it did not run.

## Escalation — when a tier is not enough

Escalate when the evidence you have could not have shown the defect:

- A logic fix that changes a rendered surface → **also tier 2**
- A fix in `HappyGymStats.Data` or any migration → **also tier 4**
- A fix to a script under `scripts/` → **tier 3, always** — never "it's a small
  change, I'll just edit it"

Two or more tiers at once is normal. Take the union of the evidence, not the
cheapest one.

## Common mistakes

- **Declaring done on a green suite that never ran the relevant tier.** The
  specific trap is tier 4; check for the `[skip]` line.
- **Fixing the test instead of the bug.** If a test asserts behaviour a change
  deliberately replaced, update it and say so. If it caught something real, fix
  the code.
- **Reading Razor to diagnose a rendering bug.** Tier 2 exists because that
  failed twice already.
- **Editing a remote script and calling it verified because `bash -n` passed.**
  Syntax was never the problem; heredoc expansion and a missing tty were.
- **Widening a verify script's pattern to make it pass.** If passing the check
  requires weakening it, the check was right and the fix is wrong.
