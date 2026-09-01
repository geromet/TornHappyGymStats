# M002: Modifier Provenance & Accuracy Gradient

**Vision:** Expand import/reconstruction to cover modifier-affecting Torn signals (personal, faction, company), track provenance completeness over time, and expose per-point accuracy confidence (red→green) in API/frontend while providing actionable operator guidance for missing owner/faction/company logs.

Architecture baseline update (2026-05-01): API and CLI consume Core only; Core owns shared fetch/reconstruction/storage orchestration and depends on Data + Visualizer. Legacy export tooling is isolated under HappyGymStats.Legacy.

## Slices

- [x] **S01: S01** `risk:high` `depends:[]`
  > After this: After this slice, we have a validated endpoint/log taxonomy matrix with required key scopes, known log IDs/categories, expected payload fields, and confidence impact mapping.

- [x] **S02: S02** `risk:high` `depends:[]`
  > After this: After this slice, DB schema supports time-bounded modifier provenance and verification states for personal/faction/company contributions.

- [x] **S03: S03** `risk:high` `depends:[]`
  > After this: After this slice, import pipeline reconstructs baseline modifier evidence and flags unresolved faction/company owner dependencies.

- [x] **S04: S04** `risk:medium` `depends:[]`
  > After this: After this slice, /api/v1/torn/surfaces/latest includes per-point confidence values and reason codes supporting red→green gradients.

- [x] **S05: S05** `risk:medium` `depends:[]`
  > After this: After this slice, point clouds color by confidence gradient with tooltips explaining evidence coverage and missing sources.

- [x] **S06: S06** `risk:medium` `depends:[]`
  > After this: After this slice, users can view actionable warnings with profile links and optionally enter manual faction/company overrides.

## Boundary Map

Not provided.
