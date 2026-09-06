# Checking the Current Remote Branch Frontier

This is a live-check procedure, not a stored branch list. The dated
[2026-09-05 inventory](BRANCH-INVENTORY-2026-09-05.md) and
[retirement provenance](RETIREMENT-PROVENANCE-2026-09-05.md) preserve historical
evidence. Neither determines what exists or is owned now.

## Read-only verification

From a clone whose `origin` is `geromet/TornHappyGymStats`:

```bash
git remote get-url origin
git ls-remote --symref origin HEAD
git ls-remote --heads origin
```

The second command discovers the actual default; do not assume its name.
Check the [open PR list](https://github.com/geromet/TornHappyGymStats/pulls)
and each PR's exact head/base. Follow any non-default base until it reaches an
open default-destined rollup. Read [#140](https://github.com/geromet/TornHappyGymStats/issues/140)
body and recent comments for ownership, gates and branch movement. Read
[#218](https://github.com/geromet/TornHappyGymStats/issues/218) for the completion
map, then verify its time-sensitive statements against GitHub.

Every useful non-default branch must be directly/transitively represented by an
open default-destined PR, proven incorporated/superseded, or explicitly abandoned
after unique-work assessment. `ahead` alone is not evidence of missing work:
squash merges often retain different commit identities.

## Historical milestones (not today's inventory)

- The three recovery PRs #213/#214/#215 and Chain Planner #194 reached default.
- #222 merged into the non-default #221 docs rollup. Check whether #221 is now
  open or landed before describing its content as default-incorporated.
- #193 records two retirement waves, 40 + 25 deletions. The six-ref observation
  after wave 2 is dated provenance, not a permanent frontier.
- The old `fix/align-prod-nginx-conf-name` local-worktree preserve record describes
  a past observation. Later remote absence does not prove how it was deleted or
  that the operator's local worktree is safe to remove.

This procedure replaces the earlier 29-ref snapshot and its active preserve/review
instructions. That snapshot remains available in Git history through child #222;
it must not be replayed as a work queue.

## Mutation boundary

Read-only inspection requires no claim. Any conflicting mutation requires the
two-phase #140 claim protocol. Remote deletion requires separate explicit cleanup
authorization and fresh local-worktree, unpushed-work, age, PR/dependency and
expected-head checks. This file authorizes neither deletion nor restoration.
Fleet/manual agents never merge to default. Do not create replacement branches
merely to preserve this incident's historical commit identities.
