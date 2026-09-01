---
verdict: needs-remediation
remediation_round: 0
---

# Milestone Validation: M004

## Success Criteria Checklist
- ✅ Code-change evidence exists: fresh verification run `.gsd/exec/35e0e79c-e87d-4c15-ba84-d64ff3a9735f.stdout` found `non_gsd_branch_diff_count=52` from merge-base `c01ad3f...` to `HEAD`, including API, Blazor, docs, scripts, and tests.
- ✅ My stats visible/auth-required menu markers passed in the final gate: `[PASS] My stats page has [Authorize]`, `[PASS] My stats nav link exists`, and `[PASS] My stats nav lock icon exists`.
- ❌ `/api/v1/torn/surfaces/me` is not sufficiently identity-map bound for the milestone/security requirement. `SurfacesController.GetMine` reads `Claims.AnonymousId` and queries caller-scoped logs directly, but unlike `ImportController.StartMyImport`, it does not verify `ClaimTypes.NameIdentifier` against the identity-map row. This violates the shared identity-map invariant expected by R003 and the operator-gate docs.
- ❌ Signed-in users are not proven to see their personal cloud in production shape. Blazor registers `SurfacesService` with a plain typed `HttpClient` and does not attach the saved OIDC access token to outgoing API requests, while the API endpoints require JWT bearer auth. The final gate's endpoint-string/static checks and fake-auth API tests do not prove Blazor-to-API auth propagation.
- ✅ Operator gate instructions exist and are docs-contract verified: final gate passed Keycloak/identity-map runbook marker checks and `scripts/verify/s08-docs-contract.sh` exited 0.

## Slice Delivery Audit
| Slice | Claimed output | Delivered / verified | Validation note |
|---|---|---|---|
| S01 | Authenticated My stats read path and `/surfaces/me` claim-bound endpoint | Code and final gate show `/my-stats`, menu lock markers, `SurfacesService` use of `/api/v1/torn/surfaces/me`, and API endpoint filtering by anonymousId claim | Needs remediation: read path lacks identity-map subject re-binding and Blazor auth propagation is not proven. S01 summary is also a placeholder from auto-mode recovery, so durable narrative is weak. |
| S02 | Authenticated My stats import API/UI path claim-bound to caller identity-map anonymousId | Verified by S02 tests and final gate; import endpoint checks anonymousId claim, subject claim, identity-map existence, subject match, body tampering, and Blazor `/import-jobs/me` endpoint usage | Delivered; its stronger identity resolver behavior should be shared with `/surfaces/me`. |
| S03 | Final gate and operator Keycloak/identity-map UAT runbook | Fresh run `.gsd/exec/35e0e79c-e87d-4c15-ba84-d64ff3a9735f.stdout` passed build, 43 tests, docs contract, final gate, and secretless provenance check | Delivered as local/static verification, but gate has blind spots around Blazor token forwarding and `/surfaces/me` identity-map semantics. |

## Cross-Slice Integration
Cross-slice integration is not fully closed. S02 established the correct ownership invariant for authenticated personal imports: claims plus identity-map subject binding. S01's read endpoint did not consume that invariant, so read and import `/me` endpoints diverge. S03's final gate verifies route strings, selected controller behavior under fake auth, docs markers, and provenance safety, but it does not prove the actual Blazor server-to-API JWT handoff needed for signed-in `/my-stats` to load data in production shape.

## Requirement Coverage
R003 remains unsupported by the assembled implementation despite being marked validated in the preloaded context. The import half of R003 is well covered, but the read/render half is blocked by (1) `/surfaces/me` trusting only `anonymous_id` without identity-map subject re-binding and (2) missing Blazor-to-API bearer token forwarding. Do not advance or rely on the validated status until remediation adds a shared identity resolver/token-forwarding proof and re-runs the final gate.

## Verification Class Compliance
Fresh verification executed in this completion turn: `bash scripts/verify/m004-my-stats-final-gate.sh`, targeted `dotnet test` filter, `bash scripts/verify/s08-docs-contract.sh`, branch diff evidence, and artifact checks all exited 0 in `gsd_exec` run `35e0e79c-e87d-4c15-ba84-d64ff3a9735f`. Additional delegated review found integration/security gaps that the automated gate did not cover. Because these gaps directly affect success criteria, the milestone needs remediation rather than completion.


## Verdict Rationale
The mechanical final gate passed, but review and direct code inspection found two success-criteria blockers: signed-in Blazor users are not proven able to call JWT-protected `/me` API endpoints because no access token is forwarded, and `/surfaces/me` does not preserve the same identity-map ownership invariant as `/import-jobs/me`. A verification gate that cannot catch those issues is insufficient for milestone completion.

## Remediation Plan
1. Add a shared authenticated-caller resolver used by both `/api/v1/torn/surfaces/me` and `/api/v1/torn/import-jobs/me`; it should require valid `anonymous_id`, require subject, load identity-map by anonymousId, return 409 `identity_setup_required` for missing map, return 403 for subject mismatch, and only then return the caller anonymousId/public key.
2. Add Blazor server-to-API bearer-token forwarding for `SurfacesService` (for example an `HttpMessageHandler` using `IHttpContextAccessor` and `GetTokenAsync("access_token")`) or replace the direct API call with a backend-for-frontend endpoint that preserves authenticated identity.
3. Expand tests/final gate to prove anonymous API calls are challenged, `/surfaces/me` missing-map and subject-mismatch behavior matches `/import-jobs/me`, the typed Blazor client sends an Authorization bearer token, and My stats no-data/read failure states are classified safely.
4. Update `docs/M004-MY-STATS-OPERATOR-GATE.md` only after code behavior and docs agree for `/surfaces/me` setup blockers.
5. Replace or annotate the placeholder S01 summary with real implementation evidence before relying on milestone artifacts for future planning.
