# M004: My stats page with auth-scoped gym cloud

**Vision:** Add an authenticated My stats experience in Blazor that shows the logged-in user’s gym point cloud via a claim-bound API endpoint, preserving existing pseudonymization and identity mapping invariants.

## Success Criteria

- My stats is visible and marked as auth-required in menu.
- /api/v1/torn/surfaces/me is auth-protected and claim-bound.
- Signed-in users see only their data in My stats point cloud.
- Operator gate instructions exist for manual Keycloak fixes when auth mapping blocks progress.

## Slices

- [x] **S01: S01** `risk:medium` `depends:[]`
  > After this: Signed-in user opens /my-stats, sees only their gym point cloud; signed-out users are challenged; endpoint is claim-bound with no PlayerID input.

- [x] **S02: S02** `risk:high` `depends:[]`
  > After this: After this: My stats exposes an authenticated import action/API path that binds Torn imports to the caller’s identity-map anonymousId, rejects cross-user ownership, and has deterministic API/service tests proving the contract.

- [x] **S03: S03** `risk:medium` `depends:[]`
  > After this: After this: fresh build/test/browser or documented UAT evidence proves /my-stats signed-out challenge, signed-in personal cloud, /surfaces/me contract, safe failure states, no secret leakage, provenance regression safety, and operator Keycloak identity-map gate instructions.

## Boundary Map

Not provided.
