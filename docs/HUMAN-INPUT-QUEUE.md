# Human input queue

A short list of things the agent fleet cannot do itself: a repo-admin action,
a credential only the operator holds, or a T3 remote/deploy step CLAUDE.md and
`docs/WORKING-AGREEMENT.md` reserve for the human. Everything else the fleet is
waiting on is implementation work, not human input, and belongs in its issue,
not here.

This file is a runbook, not a plan. It tracks *why a human has to be the one to
press the button*, not *what the feature does* — that's the linked issue.

## How to use it

Run the scripts in `scripts/human-blockers/` in order, in one sitting:

```bash
bash scripts/human-blockers/00-run-all.sh
```

Each step is read-only or explicitly asks before doing anything that mutates
shared state, and stops with a `gh issue comment` command ready to paste so the
result reaches the fleet on its next hourly pass. Nothing here runs a
`scripts/deploy*.sh`, `scripts/menu.sh`, or other T3 script on your behalf —
see item 3.

Re-run `00-run-all.sh` whenever this file gains a new item; it walks the
numbered scripts in this directory in order, so a new `0N-*.sh` is picked up
without editing the runner.

## Queue

### 1. Confirm a live Torn API key is available for the #104 gates

**Blocks:** #104 (M010/M012/M013 stop gates), specifically M010's requirement
to compare the FF formula against real roster stats. The fleet's cloud agents
have no Torn credentials and no route to `api.torn.com`; only a session with
your `.env` (this one, or you locally) can make that call.

**Script:** `scripts/human-blockers/01-verify-torn-key.sh` — reads
`TORN_API_KEY` from `.env`, makes one read-only `GET /v2/user/basic` call, and
reports the key's access level without printing the key itself. If it's
missing, expired, or the wrong access level, the script tells you what to
paste into `.env`.

Already run once (2026-09-04): the key currently in `.env` comes back
`{"error":{"code":2,"error":"Incorrect key"}}` — it's stale/wrong, not just
unverified. Get a fresh Limited-access key and update `.env` before this item
is actually done.

**Not yet actionable beyond this:** the M010/M012/M013 comparison harnesses
don't exist as code yet — that's fleet work, tracked in #104 itself. This step
only unblocks the credential; building and running the comparison is the
fleet's next move once it has a confirmed-good key to use.

### 2. Turn on required status checks for `main` (#56)

**Blocks:** #56 directly — right now `main` has **no branch protection at
all** (confirmed via `gh api repos/.../branches/main/protection` → 404), so a
red or stale PR can still be merged by hand. Configuring a GitHub ruleset is a
repository-admin action; the fleet's PAT (if it has one at all) shouldn't be
the thing that can rewrite the repo's own merge policy.

**Prerequisite:** #56's own triage says wait for #57 to stabilize first. #57
is currently PR #125, which has an open, unaddressed review finding as of this
writing — do not run step 2 until #125 is fixed and merged.

**Script:** `scripts/human-blockers/02-branch-protection-setup.sh` — resolves
the exact check-run context names from a recent green commit, backs up the
current ruleset to a local file, prints the exact `gh api` PATCH body #56
specifies, and stops before sending it. Review the printed diff, then re-run
with `--apply` to send it. It refuses to run at all until #125 is merged
(checks live PR state each time).

### 3. Optional: deploy merged runtime changes to production

**Not fleet-blocking** — nothing in the queue is waiting on this; it's here
only because you said "do it all in a row." #122 (removing the placeholder
Faction page) is a rendered Blazor change and won't be visible to real users
until deployed.

**Script:** `scripts/human-blockers/03-deploy-checklist.sh` — prints the
commands, does not run them. Per `docs/WORKING-AGREEMENT.md` §5, run them
yourself:

```bash
! bash scripts/menu.sh
```

and pick the frontend deploy task (dry run first — it's dry-run by default).

## Reporting back to the fleet

Each script above ends by printing a ready-to-run `gh issue comment ...`
command with the result already filled in (key access level, ruleset diff
applied, deploy SHA). Run the ones you're satisfied with; that's the signal
the next hourly review pass reads. Nothing posts itself — you decide what's
worth telling the fleet.
