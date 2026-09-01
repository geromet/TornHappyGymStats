---
estimated_steps: 4
estimated_files: 4
skills_used:
  - write-docs
  - api-design
  - verify-before-complete
---

# T02: Publish operator Keycloak gate and UAT evidence docs

Write cold-reader documentation for operators who must validate or repair Keycloak/identity-map readiness before approving My stats, including safe manual remediation steps and a UAT evidence checklist that does not require storing secrets in git. Executor skills to load: `write-docs`, `api-design`, `verify-before-complete`.

Steps:
1. Draft `docs/M004-MY-STATS-OPERATOR-GATE.md` for the reader: a production operator validating My stats before milestone closure.
2. Include setup blockers, signed-out challenge, signed-in personal cloud, safe failure states, secret redaction rules, and sanitized UAT evidence fields.
3. Link the runbook from `docs/SETUP.md` and `README.md`.
4. Extend `scripts/verify/s08-docs-contract.sh` with required marker checks for the M004 runbook.

Must-Haves:
- The doc gives actionable Keycloak/identity-map gate instructions without asking operators to commit secrets or paste tokens into tracked files.
- The docs contract verifier fails if private endpoint names, `identity_setup_required`, signed-out behavior, or redaction guidance disappear.

Failure Modes (Q5): live Keycloak/API failures are documented as safe UAT blockers with categorized smoke checks; malformed/failed HTTP responses require sanitized excerpts only.
Load Profile (Q6): shared resources are Keycloak admin state, identity-map database rows, and API health endpoints; 10x risk is operator error or leaked secrets, mitigated with placeholders and redaction instructions.
Negative Tests (Q7): signed-out challenge, missing map/409, mismatch/403, expired session/401, no-data empty state, API unavailable/502, and Torn API key redaction.

## Inputs

- `docs/SETUP.md`
- `README.md`
- `scripts/verify/s08-docs-contract.sh`
- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`

## Expected Output

- `docs/M004-MY-STATS-OPERATOR-GATE.md`
- `docs/SETUP.md`
- `README.md`
- `scripts/verify/s08-docs-contract.sh`

## Verification

bash scripts/verify/s08-docs-contract.sh && test -s docs/M004-MY-STATS-OPERATOR-GATE.md && rg -n "signed-out|identity_setup_required|/api/v1/torn/surfaces/me|/api/v1/torn/import-jobs/me|Torn API key|Keycloak" docs/M004-MY-STATS-OPERATOR-GATE.md
