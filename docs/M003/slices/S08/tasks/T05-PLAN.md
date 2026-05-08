---
estimated_steps: 7
estimated_files: 6
skills_used: []
---

# T05: Add docs current-state drift verifier

Why: Documentation drift caused part of this audit. A lightweight verifier should catch stale route/service/path claims.

Do:
1. Add a docs contract verifier checking required terms, current routes, service names, and absence of known stale primary claims.
2. Check README, Overview, Setup, Deployment, and `.http` examples.
3. Include the audit report path or milestone ID as a required reference.
4. Keep checks deterministic and explicit, similar to prior taxonomy drift scripts.

Done when: one command fails fast if docs drift back to old SQLite/static route assumptions.

## Inputs

- `scripts/verify-s01-taxonomy.sh`
- `README.md`
- `docs/OVERVIEW.md`
- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`

## Expected Output

- `scripts/verify/s08-docs-contract.sh`

## Verification

bash scripts/verify/s08-docs-contract.sh

## Observability Impact

Signals added/changed: docs drift verifier.
How a future agent inspects this: run S08 verifier.
Failure state exposed: stale route/storage/frontend claims.
