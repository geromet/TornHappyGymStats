# Overview

HappyGymStats is a Torn data pipeline centered on **import → reconstruct → surfaces**.

This repository contains the API/CLI/core/data pipeline code. Production runtime also includes UI and auth boundaries that are deployed/verified alongside this repo.

## Current architecture (code ownership)

Primary projects in `HappyGymStats.sln`:

- `src/HappyGymStats.Api` — ASP.NET Core API for import jobs, health, paginated reads, and surfaces endpoints.
- `src/HappyGymStats.Cli` — local/ops entrypoint for fetch and reconstruction workflows.
- `src/HappyGymStats.Core` — Torn log fetch + reconstruction logic (`HappyTimelineReconstructor`, `SurfaceSeriesBuilder`, etc.).
- `src/HappyGymStats.Data` — EF Core data model and persistence layer used by API and import pipeline.
- `src/HappyGymStats.Legacy` / `src/HappyGymStats.Visualizer` — compatibility/export and visualization support.

Operational runtime peers (not present as projects in this checkout, but part of deployment and smoke contracts):

- **Blazor frontend** (user-facing app)
- **AdminPanel** (operations/support surface)
- **Identity/Keycloak** (auth boundary for protected surfaces)
- **PostgreSQL** (production DB provider path validated by integration tier)

## Canonical data flow

1. **Import**
   - `POST /api/v1/torn/import-jobs` queues an import.
   - `ImportService` fetches Torn user log pages and appends/stores raw rows.
2. **Reconstruct**
   - Core reconstruction derives gym trains and happy events from raw logs.
   - Derived datasets are persisted via `HappyGymStats.Data` entities.
3. **Surfaces**
   - `SurfacesCacheWriter` materializes `meta.json` and `latest.json` surfaces artifacts.
   - API serves cached artifacts at:
     - `GET /api/v1/torn/surfaces/meta`
     - `GET /api/v1/torn/surfaces/latest`
4. **Consumers**
   - Frontends (Blazor/Admin-facing flows) and diagnostics consume API + surfaces contracts.

## Runtime boundaries and ports

Documented host boundaries used by deployment/smoke verification:

- **API loopback:** `127.0.0.1:5047`
- **Blazor loopback:** `127.0.0.1:5182`
- **AdminPanel loopback:** `127.0.0.1:5048`
- **External API route:** `/api/*` proxied to API backend
- **AdminPanel nginx health proxy:** `/admin/health` → `127.0.0.1:5048/admin/health`

These boundaries are enforced by deployment scripts and smoke contracts under `scripts/verify/` and `infra/nginx-adminpanel.conf`.

## Legacy/interchange artifacts

The following remain as compatibility/interchange surfaces, not the primary architecture definition:

- **JSONL/raw log artifacts** used during import/reconstruction workflows.
- **SQLite-backed local/dev paths** still used by the API default configuration and local verification scripts.
- **Local surfaces cache artifacts** (notably `web/data/surfaces/*.json`) used as generated interchange outputs and local verification targets.

Treat these as migration-compatible data interchange and local-operability paths. The canonical contract for planning work is the import/reconstruct/surfaces pipeline plus the runtime boundaries above.
