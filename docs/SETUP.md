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
  - Intended command filter: `dotnet test --filter "PostgresApiIntegration"`
  - Requires Docker daemon availability (local Docker Desktop/Engine or CI Docker service)
  - If Docker is unavailable, Postgres provider tests are expected to skip with a clear setup message rather than fail with a generic connection exception

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
