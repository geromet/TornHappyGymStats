---
estimated_steps: 22
estimated_files: 4
skills_used: []
---

# T03: Add automated frontend confidence visualization regression tests

---
estimated_steps: 5
estimated_files: 4
skills_used:
  - test
  - verify-before-complete
---

# T03: Add automated frontend confidence visualization regression tests

**Slice:** S05 — Frontend Confidence Visualization
**Milestone:** M002

## Description

Create deterministic tests for gradient mapping, fallback handling, and tooltip reason messaging against generated/fixture payloads.

## Inputs

- `web/app.js` — helper functions and trace construction from T02.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — upstream confidence contract.
- `web/data/surfaces/latest.json` — local generated payload from T01.

## Expected Output

- `tests/web/confidence-visualization.test.mjs` — frontend visualization contract tests.
- `tests/fixtures/surfaces/latest-confidence-sample.json` — tracked fixture payload.
- `web/app.js` — exported pure helpers for testability (if needed).

## Verification

- `node --test tests/web/confidence-visualization.test.mjs && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`

## Inputs

- ``web/app.js``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``web/data/surfaces/latest.json``

## Expected Output

- ``tests/web/confidence-visualization.test.mjs``
- ``tests/fixtures/surfaces/latest-confidence-sample.json``
- ``web/app.js``

## Verification

node --test tests/web/confidence-visualization.test.mjs && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"
