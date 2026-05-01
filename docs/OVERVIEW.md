# Overview

HappyGymStats is a Torn data toolchain with two user surfaces:

- **CLI** for fetch/reconstruct/export workflows
- **API + static dashboard** for hosted visualization

Core flow:
1. Import Torn user log data (`/user/log`, gym/happy categories)
2. Reconstruct happy timeline and derived gym/event datasets
3. Persist results in SQLite
4. Serve read-only endpoints consumed by `web/`

## Components

- `src/HappyGymStats.Cli` — interactive CLI workflows
- `src/HappyGymStats.Api` — ASP.NET Core API (`/api/v1/torn/*`)
- `src/HappyGymStats.Core` — fetch + reconstruction logic
- `src/HappyGymStats.Data` — EF Core + SQLite storage model
- `web/` — static frontend for charts/import trigger

## Deployment shape

Production uses same-host routing:
- Frontend: `https://torn.geromet.com`
- API: `https://torn.geromet.com/api/...`

Nginx proxies `/api/*` to backend (`127.0.0.1:5047`).
