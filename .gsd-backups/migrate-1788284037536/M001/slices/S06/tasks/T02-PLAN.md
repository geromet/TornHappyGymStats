---
estimated_steps: 2
estimated_files: 3
skills_used: []
---

# T02: Align API request examples and docs verification with current endpoint contract

Skills to load: `write-docs`, `verify-before-complete`.

Update the API request/example doc surface to match current endpoint contract and ordering, including import status retrieval by id, pagination semantics on read endpoints, and practical operator notes for validating behavior quickly. Add lightweight executable doc-verification commands so future changes can mechanically confirm required sections/endpoints remain documented.

## Inputs

- ``src/HappyGymStats.Api/HappyGymStats.Api.http``
- ``README.md``
- ``src/HappyGymStats.Api/Program.cs``

## Expected Output

- ``src/HappyGymStats.Api/HappyGymStats.Api.http``
- ``README.md``

## Verification

rg -n "POST .*\/v1\/import|GET .*\/v1\/import\/latest|GET .*\/v1\/import\/\{id\}|limit=|cursor=" src/HappyGymStats.Api/HappyGymStats.Api.http && rg -n "import/latest|import/\{id\}|no-auth|deferred" README.md
