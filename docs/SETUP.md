# Setup & Run

## Prerequisites
- .NET 8 SDK

## Build & test

```bash
dotnet build
dotnet test
```

### Test tiers

- **Default unit/integration-lite tier (SQLite/in-memory)**
  - Runs with plain `dotnet test`
- **Postgres provider integration tier (Testcontainers)**
  - Canonical verifier: `bash scripts/verify/s07-postgres-integration.sh`
  - Under the hood it runs: `dotnet test --filter "Category=PostgresApiIntegration"`
  - Requires Docker daemon availability (local Docker Desktop/Engine or CI Docker service)
  - Intentional skip switch: set `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1`
  - Startup timeout control: `HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS` (default 90, range 15-600)
  - Verifier emits explicit outcomes: `SKIP` (Docker unavailable or intentional), `FAIL` (test or timeout), `PASS` (provider checks succeeded)

Why this tiering exists: production uses Postgres/Npgsql paths that SQLite-only tests cannot fully prove.

## Run API locally

```bash
dotnet run --project src/HappyGymStats.Api
```

Default API DB path:
- `src/HappyGymStats.Api/data/happygymstats.db`

Override DB path:
- `HAPPYGYMSTATS_DATABASE=/absolute/path/to/happygymstats.db`
- `ConnectionStrings__HappyGymStats=/absolute/path/to/happygymstats.db`

## Run static frontend locally

```bash
python3 -m http.server 8000 --directory web
```

Open: `http://localhost:8000`
