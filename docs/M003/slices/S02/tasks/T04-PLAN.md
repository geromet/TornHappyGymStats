---
estimated_steps: 7
estimated_files: 3
skills_used: []
---

# T04: Test Blazor API failure classification

Why: Classifier behavior should survive refactors, and S02 needs mechanical verification before browser/production smoke exists.

Do:
1. Add tests for service/classifier mapping of 404, 502, other 5xx, validation 422/400, invalid JSON, and success.
2. Use fake `HttpMessageHandler` or a small helper to avoid network calls.
3. Verify import and load paths do not include request secrets in exception messages.
4. Add a slice verifier script if needed to combine build and targeted tests.

Done when: API failure classification has automated coverage for the reported 502 class and no-data behavior.

## Inputs

- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`

## Expected Output

- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
- `scripts/verify/s02-blazor-api-boundary.sh`

## Verification

bash scripts/verify/s02-blazor-api-boundary.sh

## Observability Impact

Signals added/changed: regression tests for diagnostic categories.
How a future agent inspects this: run S02 verifier/targeted test.
Failure state exposed: incorrect category mapping or secret leakage in messages.
