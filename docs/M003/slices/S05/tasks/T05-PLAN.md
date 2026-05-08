---
estimated_steps: 7
estimated_files: 3
skills_used: []
---

# T05: Document and verify smoke script contract

Why: The smoke script is the milestone’s main operational proof and must be documented and locally verifiable before being wired into deploy scripts.

Do:
1. Add a local verifier for smoke script syntax and required check tokens.
2. Document how to run the smoke script, required privileges, optional checks, and expected failure categories.
3. If a local dry-run/no-remote mode exists, verify it in the script.
4. Ensure the script is executable and shellcheck-compatible where possible.

Done when: smoke script can be trusted as the canonical post-deploy diagnostic entrypoint.

## Inputs

- `scripts/verify/production-smoke.sh`

## Expected Output

- `scripts/verify/s05-production-smoke-contract.sh`
- `docs/DEPLOYMENT.md`

## Verification

bash scripts/verify/s05-production-smoke-contract.sh

## Observability Impact

Signals added/changed: contract verifier for smoke script drift.
How a future agent inspects this: run S05 verifier.
Failure state exposed: missing required smoke phase/check/documentation.
