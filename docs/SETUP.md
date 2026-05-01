# Setup & Run

## Prerequisites
- .NET 8 SDK

## Build & test

```bash
dotnet build
dotnet test
```

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
