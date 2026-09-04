---
name: looking-at-the-app
description: Use when a change affects what the HappyGymStats war board, Blazor pages or any rendered surface looks like, when asked to run or screenshot the app, or before calling any UX issue finished.
---

# Looking at the app

## Overview

**A UX slice is not done until someone has looked at it, and that someone should
not have to be the operator.**

U001 shipped a caption reading `Last hit (inferred) inferred` and an operator
diagnostic rendered inside a user-facing error banner. Both were invisible in the
Razor source and obvious in the first rendered frame. Reading markup is not
looking.

## The command

    bash scripts/screenshot-board.sh

Boots the API and the Blazor host locally with development authentication and the
seeded war, shoots phone / tablet / desktop in light and dark, then stops both.
Output lands in `workspace/tmp/screenshots/` — gitignored, regenerate rather than
commit. Nothing touches the server, and no browser you use personally is involved.

Then **read the images** with the Read tool. A screenshot you did not open proves
nothing.

## Quick reference

| Need | Command |
|---|---|
| Shoot the war board | `bash scripts/screenshot-board.sh` |
| A different route | `SHOT_ROUTE=/faction bash scripts/screenshot-board.sh` |
| Iterate without restarting hosts | `bash scripts/screenshot-board.sh --keep-running` |
| Is the tooling installed? | `bash scripts/screenshot-board.sh --check` |
| First-time install | `bash scripts/screenshot-board.sh --setup` |

`--setup` creates `.venv/` and downloads Chromium (~115 MB) into
`~/.cache/ms-playwright`. No sudo. Run `--check` first; only run `--setup` if it
reports something missing.

Ports default to API 5047 / web 5137 (`SHOT_API_PORT`, `SHOT_WEB_PORT`). The
driver is `scripts/ux/shoot.py`; point it at an already-running instance when
iterating.

## Common mistakes

- **Reasoning about the Razor file instead of shooting it.** The two U001 defects
  were both invisible that way. If the change is visible, look at it.
- **Setting dev auth on only one host.** The board then renders "War board
  unavailable. Authentication is required" — the dev-header principal has no access
  token to forward to the API. The script sets it on both; don't hand-roll a
  replacement that sets it on one.
- **Committing the screenshots.** `workspace/` is gitignored on purpose.
- **Stopping at desktop.** War nights happen on phones — that is why U003 exists
  and why the script shoots three viewports.

## Beyond a picture

Playwright is here rather than a headless-screenshot one-liner because the
remaining UX slices need interaction: U003 needs a real viewport, U005 needs Tab
and focus rings, U006 needs `prefers-color-scheme` emulation. Extend
`scripts/ux/shoot.py` for those rather than reaching for a different tool.
