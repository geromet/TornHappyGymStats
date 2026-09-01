---
estimated_steps: 7
estimated_files: 3
skills_used: []
---

# T04: Add S01 local contract verifier

Why: The slice needs one command that proves the API path locally before production credentials/server access are involved.

Do:
1. Add or update a verification script for S01 that syntax-checks deploy scripts, builds/tests relevant API tests, and statically verifies required health gate tokens.
2. Include checks for the gotcha: `dotnet run` verification that pins `ASPNETCORE_URLS` must use `--no-launch-profile`.
3. Avoid making remote calls by default; allow production URL checks only through an explicit opt-in variable if implemented.
4. Document the command in the slice plan or deployment docs.

Done when: a future agent can run one local command and know whether S01 code/config contracts are present.

## Inputs

- `scripts/verify/s05-local-surfaces.sh`
- `scripts/verify/build-and-test.sh`
- `scripts/deploy-backend.sh`

## Expected Output

- `scripts/verify/s01-api-production-contract.sh`

## Verification

bash scripts/verify/s01-api-production-contract.sh

## Observability Impact

Signals added/changed: deterministic local verifier for API deploy contract drift.
How a future agent inspects this: run `bash scripts/verify/s01-api-production-contract.sh`.
Failure state exposed: missing health gate token, stale route, missing --no-launch-profile when relevant.
