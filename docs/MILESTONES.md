# War-command milestones — moved to GitHub issues

**GitHub issues are authoritative for this work.** This file is a pointer.

It used to hold the `M007`–`M013` breakdown derived from the hand-off pack in
`workspace/V2/`. That breakdown now lives in issues, so there is one place to
look rather than two that drift.

| Milestone | Subject | Issue |
|---|---|---|
| M007 | Conformance sweep, M1/M2 acceptance gaps | **done** — merged 2026-09-03 |
| M008 | Chain command | [#87](https://github.com/geromet/TornHappyGymStats/issues/87) (chain-endpoint gate done, see [#104](https://github.com/geromet/TornHappyGymStats/issues/104)) |
| M009 | Member linking and the key vault | [#80](https://github.com/geromet/TornHappyGymStats/issues/80) — **compliance blocker** |
| M010 | Targeting, λ*, and hit calling | [#83](https://github.com/geromet/TornHappyGymStats/issues/83) |
| M011 | The userscript | [#84](https://github.com/geromet/TornHappyGymStats/issues/84) |
| M012 | Comms, timeline, and the strategy map | no issue yet — gear-tracker spike in [#104](https://github.com/geromet/TornHappyGymStats/issues/104) |
| M013 | The Investigator | [#91](https://github.com/geromet/TornHappyGymStats/issues/91) |

The epic is [#92](https://github.com/geromet/TornHappyGymStats/issues/92).

## What did not move, because it must not be missed

**The gates are [#104](https://github.com/geromet/TornHappyGymStats/issues/104).**
A gate is not a task: it is a point where work stops, a finding is recorded, and
the outcome is published whether or not it is favourable. Three of the four can
halt their milestone. Folded into a feature issue they read as acceptance
criteria and get quietly satisfied rather than run.

**The two standing non-goals are in `CLAUDE.md`** and apply to every milestone:
no game actions against Torn, ever; and the `Ecies` scheme must not back the war
key vault.

## Reading the sources

`workspace/V2/` is the authoritative hand-off pack — **cite `workspace/V2/...`
paths only.** The older copy at `workspace/handoff/` is a stale subset missing
documents 05–11. `workspace/` is gitignored working material, and the GSD state
under `workspace/archive/GSD/` belongs to an external tool and is deliberately
stale; do not edit it.
