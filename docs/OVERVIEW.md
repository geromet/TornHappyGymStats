# Overview

HappyGymStats is a Torn data pipeline centered on **import → reconstruct → surfaces**, plus a live ranked-war board. All production surfaces live in this repository; external runtime peers are Keycloak (auth) and PostgreSQL (production database).

## Current architecture (code ownership)

Projects in `HappyGymStats.sln` (all target `net10.0`):

- `src/HappyGymStats.Api` — ASP.NET Core API for import jobs, health, paginated reads, surfaces endpoints, and the SignalR war hub; hosts the background import pipeline.
- `src/HappyGymStats.Blazor` (+ `HappyGymStats.Blazor.Client`) — primary frontend for production user flows (Blazor Web App with WASM client for client-held-key crypto).
- `src/HappyGymStats.AdminPanel` — operations/admin surface (read-only, role-gated API).
- `src/HappyGymStats.Core` — Torn log fetch + reconstruction logic (`HappyTimelineReconstructor`, `SurfaceSeriesBuilder`, etc.) plus the war engines (`WarStateDerivationEngine`, `ChainTracker`, `OpponentProfileEngine`) and the Torn API client/rate limiter.
- `src/HappyGymStats.Data` — EF Core data model and persistence layer used by API, WarPoller, and AdminPanel.
- `src/HappyGymStats.Contracts` — shared entities, repository interfaces, and cross-project contracts.
- `src/HappyGymStats.Identity` — Keycloak JWT validation + provisional-token auth extensions.
- `src/HappyGymStats.Encryption` — ECIES + key-wrapping primitives for the pseudonymization layer.
- `src/HappyGymStats.WarPoller` — console host that polls Torn war state into Postgres and notifies the API.

Operational runtime peers (deployed alongside, not projects in this repo):

- **Identity/Keycloak** — auth boundary for protected surfaces (OIDC for Blazor, JWT bearer for API/AdminPanel).
- **PostgreSQL** — production database provider path, validated by the Testcontainers integration tier.

## Canonical data flow

1. **Import**
   - `POST /api/v1/torn/import-jobs` queues an import (202; processed by the hosted `ImportOrchestrator`).
   - `ImportOrchestrator` fetches Torn user log pages and appends/stores raw rows.
2. **Reconstruct**
   - Core reconstruction derives gym trains and happy events from raw logs.
   - Derived datasets are persisted via `HappyGymStats.Data` entities.
3. **Surfaces**
   - `SurfacesCacheWriter` materializes `meta.json` and `latest.json` surfaces artifacts.
   - API serves cached artifacts at:
     - `GET /api/v1/torn/surfaces/meta`
     - `GET /api/v1/torn/surfaces/latest`
4. **Consumers**
   - The Blazor frontend (and admin-facing flows) consume API + surfaces contracts; war-board clients consume the SignalR hub.

## Runtime boundaries and ports

Documented host boundaries used by deployment/smoke verification:

- **API loopback:** `127.0.0.1:5047`
- **Blazor loopback:** `127.0.0.1:5182`
- **AdminPanel loopback:** `127.0.0.1:5048`
- **External API route:** `/api/*` proxied to API backend
- **AdminPanel nginx health proxy:** `/admin/health` → `127.0.0.1:5048/admin/health`

These boundaries are enforced by deployment scripts and smoke contracts under `scripts/verify/` and `infra/nginx-adminpanel.conf`.

## Interchange artifacts

The following remain as local/dev interchange surfaces, not the primary architecture definition:

- **SQLite-backed local/dev paths** used by dev-auth mode and local verification scripts (production is Postgres-only).
- **Local surfaces cache artifacts** (notably `web/data/surfaces/*.json`, gitignored) used as generated interchange outputs and local verification targets (`scripts/verify/s05-local-surfaces.sh`, `scripts/verify/s06-provenance-warnings.sh`).
- **JSONL sidecar stores** (`DerivedGymTrainStore`, `DerivedHappyEventStore`) retained in Core as legacy reconstruction outputs.

Treat these as migration-compatible data interchange and local-operability paths. The canonical contract for planning work is the import/reconstruct/surfaces pipeline plus the runtime boundaries above.
