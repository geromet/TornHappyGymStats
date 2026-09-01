---
estimated_steps: 22
estimated_files: 3
skills_used: []
---

# T02: Implement confidence gradient + reason tooltip transformation in gym cloud renderer

---
estimated_steps: 6
estimated_files: 3
skills_used:
  - frontend-design
  - test
---

# T02: Implement confidence gradient + reason tooltip transformation in gym cloud renderer

**Slice:** S05 — Frontend Confidence Visualization
**Milestone:** M002

## Description

Wire S04 confidence metadata into the static frontend so each gym point color reflects confidence and tooltip copy explains provenance coverage/missing sources.

## Inputs

- `web/app.js` — existing rendering/fetch flow to extend.
- `web/index.html` — chart containers and script wiring.
- `web/data/surfaces/latest.json` — generated local payload from T01.

## Expected Output

- `web/app.js` — confidence-to-color and tooltip transformations wired into gym cloud trace.
- `web/index.html` — optional tooltip/help text adjustments.
- `web/styles.css` — optional legend styling.

## Verification

- `node --test tests/web/confidence-visualization.test.mjs`

## Inputs

- ``web/app.js``
- ``web/index.html``
- ``web/data/surfaces/latest.json``

## Expected Output

- ``web/app.js``
- ``web/index.html``
- ``web/styles.css``

## Verification

node --test tests/web/confidence-visualization.test.mjs
