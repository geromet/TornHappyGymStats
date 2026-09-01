# M001: Core/API Decoupling and DB-Native Pipeline Hardening

**Vision:** Make Core the single runtime owner for fetch/reconstruction, make import/reconstruction state durable, harden data consistency and performance characteristics, and align test/docs with the DB-native anonymous aggregate model.

## Success Criteria

- Core is the single source of truth for runtime pipeline primitives.
- Import/reconstruction status is durable and restart-safe.
- Derived dataset writes are atomic and read-consistent.
- DB-native end-to-end tests cover import→reconstruct→read contract.
- Documentation matches implemented DB-native architecture and model.

## Slices

- [x] **S01: S01** `risk:high` `depends:[]`
  > After this: API and CLI both compile/run using shared Core primitives only, with duplicate implementations removed.

- [x] **S02: S02** `risk:high` `depends:[]`
  > After this: After restarting API, /v1/import/latest and /v1/import/{id} still show accurate run history from DB.

- [x] **S03: S03** `risk:medium` `depends:[]`
  > After this: Derived data refresh no longer exposes an empty-table window during reconstruction.

- [x] **S04: S04** `risk:medium` `depends:[]`
  > After this: Large synthetic dataset benchmark shows bounded reconstruction time and documented baseline.

- [x] **S05: S05** `risk:medium` `depends:[]`
  > After this: Test suite validates DB-native end-to-end behavior without relying on legacy CLI export parity tests.

- [x] **S06: S06** `risk:low` `depends:[]`
  > After this: README/API docs describe actual DB-native behavior, no-auth aggregate model, and operational constraints.

## Boundary Map

- **In scope:** Core/API/Data runtime ownership, durable status, transactional reconstruction writes, perf baseline, DB-native tests, docs alignment.
- **Out of scope (deferred):** CORS/write-surface hardening and rate limits (prior audit step 4), auth model changes, multi-tenant user identity design.
