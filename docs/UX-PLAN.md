# UX plan — moved to GitHub issues

**GitHub issues are authoritative for this work.** This file is a pointer, kept
so that links to it from code and scripts still land somewhere useful.

It used to hold slices `U001`–`U006`. Keeping a second plan alongside the issues
meant two documents describing the same work, drifting apart, with the stale one
being whichever nobody was watching. The reasoning that lived here — why three
provenance kinds and not four, why a verifier pins a component rather than a
word list, and what U001 actually shipped — was migrated into the issues before
this file was reduced. It is in the comment thread on the epic, not lost.

| Slice | Status | Issue |
|---|---|---|
| U001 — honest-signal pass on `War.razor` | DONE 2026-09-04 | record on [#94](https://github.com/geromet/TornHappyGymStats/issues/94) |
| U001b — the same pass over `WarScout` | not started | [#105](https://github.com/geromet/TornHappyGymStats/issues/105) |
| U002 — empty, first-run and error states | partly done (#54) | [#106](https://github.com/geromet/TornHappyGymStats/issues/106) |
| U003 — mobile and the war-night layout | not started | [#97](https://github.com/geromet/TornHappyGymStats/issues/97) |
| U004 — sign-in and the admin surface | not started | [#98](https://github.com/geromet/TornHappyGymStats/issues/98) |
| U005 — contrast, focus, keyboard paths | not started | [#95](https://github.com/geromet/TornHappyGymStats/issues/95) |
| U006 — visual coherence | not started | [#95](https://github.com/geromet/TornHappyGymStats/issues/95) |

The epic is [#94](https://github.com/geromet/TornHappyGymStats/issues/94); the
redesign issues are [#95](https://github.com/geromet/TornHappyGymStats/issues/95)–[#103](https://github.com/geromet/TornHappyGymStats/issues/103).

## Two rules that outlived the plan

They are repeated here because code and verify scripts cite this file by name,
and because both were learned the expensive way.

**An estimate and a fact must never look the same.** The vocabulary is exactly
three words — measured, projected, inferred — and the test is *would this number
change if you polled again right now with nothing else having happened?*
Measured figures carry no marker: marking everything is the same as marking
nothing. `Components/Shared/FigureKind.cs` is the single definition.

**A UX slice is not done until someone has looked at it**, and that someone
should not have to be the operator. `bash scripts/screenshot-board.sh` exists
because U001 shipped a caption reading "Last hit (inferred) inferred" and an
operator diagnostic inside a user-facing error banner — both invisible in the
source, both obvious in the first rendered frame. `SHOT_NO_WAR=1` reaches the
empty board.
