---
name: writing-a-verify-script
description: Use when adding or changing a check under scripts/verify/ in HappyGymStats, when a slice needs its acceptance pinned so it cannot silently regress, or when an existing verify script fails and it is unclear whether the script or the code is wrong.
---

# Writing a verify script

## Overview

A verify script is how this repo remembers a decision. Every slice gets one, it
runs offline, and `scripts/verify/build-and-test.sh` runs the whole set before
anything is handed back.

**Core principle: pin the mechanism, not the wording.** A check that makes you
weaken the thing it protects is worse than no check.

## The principle, from the case that produced it

U001 says an estimate must never look like a fact. The obvious verifier — grep
the page for weasel words like `approx` or `~` — would have fired on the inferred
chain timer's `~mm:ss ago (±30s)`, which is correct, already shipped, and exactly
the honesty the rule is about.

So `u001-honest-signal.sh` pins the **component** instead: every war-board figure
renders through `<Figure>`, which cannot omit the marker. The failure mode being
prevented is not a bad word; it is the next person adding a panel that quietly
prints a number.

Ask of every check: *what would a well-meaning future change do wrong?* Pin that.

## Shape

Copy `scripts/verify/u001-honest-signal.sh`. Every script has:

```bash
#!/usr/bin/env bash
# <id>-<slug>.sh — one line saying what it pins, citing the plan (docs/UX-PLAN.md, U00n).
#
# WHAT THIS PINS, AND WHY IT IS SHAPED THIS WAY
# <the check you rejected, and why the shipped shape survives a good-faith change>
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TARGET="${ROOT_DIR}/src/…"

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

case "${1:-}" in
  -h|--help) printf 'Usage: bash scripts/verify/<name>.sh\n\n<one line>\n'; exit 0 ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

[[ -f "${TARGET}" ]] || fail "missing ${TARGET#"${ROOT_DIR}/"}"
pass "files present"
```

Then one `rg`-based assertion per decision, each followed by a `pass` line, each
`fail` message naming **what changed and where the rule is written down** — not
just "assertion failed".

## Rules

| Rule | Why |
|---|---|
| Offline, no host, no network | It runs in `build-and-test.sh` on every hand-back |
| Existence check before content checks | A moved file otherwise reports as a broken rule |
| `rg`, not `grep` | Matches the existing scripts; `-U` for multiline |
| Exempt by name, never by loosening the pattern | A widened regex stops catching the next case |
| Comment *why* each assertion exists | The next reader must be able to tell a real regression from a stale check |
| Count-based checks for closed vocabularies | e.g. `FigureKind` has exactly three values — a fourth must be a deliberate edit |

## Register it, or the console check fails

`scripts/verify/menu-contract.sh` runs `menu.sh --audit`, which fails on any script
in `scripts/` that is neither driven by the console nor excluded **with a written
reason**. Two valid paths, both in `scripts/lib/registry.sh`:

- **Driven** — add a record to `REG_ENTRIES`. Fields are documented at the top of
  that file; a read-only check uses `"NONE"` for the apply args.
- **Excluded** — add a `name:reason` entry to `REG_EXCLUDED`. Use this for a script
  an operator would never pick from a menu (a helper sourced by another script, a
  check that only makes sense inside `build-and-test.sh`). The reason is shown by
  `--audit`, so write it for the person who will wonder why.

Guessing wrong is cheap to detect: `bash scripts/menu.sh --audit` tells you which
scripts are uncovered and lists the deliberate exclusions.

Then add the script to `scripts/verify/build-and-test.sh` if it should gate every
hand-back.

## Verifying the verifier

A check that has never failed has never been tested. Before you are done:

1. Run it — it passes.
2. Break the thing it protects (edit the source file, do **not** commit), run it —
   it fails, and the message tells you what to fix.
3. Restore immediately: `git checkout -- <path>`, then confirm `git status` is
   clean before doing anything else.

Step 3 is not optional. An interrupted session that skipped it leaves the user a
broken working tree with no note saying why.

## Common mistakes

- **Grepping for a word.** Words move, get translated, get legitimately used by
  the correct implementation. Pin the type, the component, or the call site.
- **A check the fix makes you weaken.** If passing the check requires making the
  feature worse, the check is wrong. Rewrite it.
- **Silent passes.** Every assertion prints a `PASS:` line, so a run reads as a
  list of what is still true.
- **Forgetting the registry row.** `build-and-test.sh` will fail on
  `menu-contract.sh`, and the cause will look unrelated.
