# Refactor: Introduce Repository Layer and Fix Layering Violations

## Context

Four concrete layering violations:

1. **Core → DbContext directly**: `LogFetcher`, `PerkLogFetcher`, `ReconstructionRunner` each take `string databasePath` and construct `HappyGymStatsDbContext` via `DbContextOptionsBuilder`. Core reaches through to EF with no abstraction.
2. **Controller bypasses service layer**: `GymTrainsController` directly injects `HappyGymStatsDbContext`, executes raw SQL cursor pagination inline.
3. **Business logic in Api**: `ImportService` (full import orchestrator) and `SurfacesCacheWriter` live in `HappyGymStats.Api`.
4. **No Repository abstraction**: Nothing sits between business logic and EF.

## Target Dependency Graph

```
HappyGymStats.Data    ← EF entities, DbContext, Migrations, Repository implementations
       ↑
HappyGymStats.Core    ← Repository interfaces, services, orchestrators
       ↑
HappyGymStats.Api     ← Controllers, DTOs, DI wiring
       ↑
HappyGymStats.Cli     ← Console entry, DI wiring

HappyGymStats.Legacy  → Core (unchanged)
HappyGymStats.Tests   → Api + Core + Data (unchanged)
HappyGymStats.Blazor  → Http only (unchanged)
```

**Key decision**: Repository *interfaces* in Core, *implementations* in `Data/Repositories/`. No new project — thin EF wrappers don't justify another `.csproj`. Core retains reference to Data (for entity types) but never uses `DbContext` or `DbContextOptionsBuilder` directly.

---

## Phase 1 — Define Repository Interfaces in Core

**New files in `src/HappyGymStats.Core/Repositories/`**

### `IUserLogEntryRepository.cs`
```csharp
Task<HashSet<string>> GetExistingLogIdsAsync(int playerId, CancellationToken ct);
Task AddRangeAsync(IEnumerable<UserLogEntryEntity> entries, CancellationToken ct);
Task UpdateHappyBeforeTrainAsync(int playerId, string logId, int? happyBeforeTrain, int? delta, CancellationToken ct);
Task<IReadOnlyList<ReconstructionLogRecord>> GetReconstructionRecordsAsync(int playerId, CancellationToken ct);
Task<IReadOnlyList<SurfaceSeriesBuilder.GymLogEntry>> GetGymLogEntriesAsync(CancellationToken ct);
Task<CursorPage<GymTrainDto>> GetGymTrainsPageAsync(int take, PageCursor? cursor, CancellationToken ct);
```

> `CursorPage<T>`, `GymTrainDto`, `PageCursor` move from `Api/Models/` → `Core/Models/` (query results, not HTTP wire format). `CursorEncoder` (Base64 wire encoding) stays in Api.

### `IImportRunRepository.cs`
```csharp
Task<ImportRunEntity> CreateAsync(ImportRunEntity run, CancellationToken ct);
Task<ImportRunEntity?> GetLatestIncompleteAsync(int playerId, CancellationToken ct);
Task UpdateAsync(ImportRunEntity run, CancellationToken ct);
Task<int> ResolvePlayerIdAsync(CancellationToken ct);  // fallback for CLI
```

### `IModifierProvenanceRepository.cs`
```csharp
Task<IReadOnlyList<ModifierProvenanceRow>> GetAllAsync(CancellationToken ct);  // projection record defined in Core
Task StageReplacementForPlayerAsync(int playerId, IEnumerable<ModifierProvenanceEntity> entities, CancellationToken ct);
```

> `StageReplacementForPlayerAsync` marks existing rows for deletion and adds new ones to the change tracker. Does **not** call `SaveChangesAsync` — caller is responsible (see `IUnitOfWork` below). This enables batching with other staged changes for atomicity.

### `IAffiliationEventRepository.cs`
```csharp
Task<HashSet<string>> GetExistingSourceLogIdsAsync(int playerId, CancellationToken ct);
Task AddAsync(AffiliationEventEntity entity, CancellationToken ct);
```

### `ILogTypeRepository.cs`
```csharp
Task<HashSet<int>> GetExistingIdsAsync(CancellationToken ct);
Task AddRangeIfMissingAsync(IEnumerable<LogTypeEntity> types, CancellationToken ct);
```

Also add in Core:
- `ModifierProvenanceRow` record (used by `SurfacesCacheWriter` — replaces anonymous type projection).
- `HappyBeforeTrainUpdate` record `(string LogId, int? HappyBeforeTrain, int? Delta)` — parameter type for reconstruction writeback.

### `IUnitOfWork.cs`
```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

`HappyGymStatsDbContext` already exposes `SaveChangesAsync` — register it as `IUnitOfWork` in DI via a pass-through:
```csharp
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
```
No extra class needed. `ReconstructionRunner` injects `IUnitOfWork` and calls it once after staging both provenance and happy updates — this is the single atomicity seam.

---

## Phase 2 — Repository Implementations in Data

**New files in `src/HappyGymStats.Data/Repositories/`**

Five sealed classes, one per interface, each injecting `HappyGymStatsDbContext` via constructor:

- `UserLogEntryRepository : IUserLogEntryRepository`
  - `GetGymTrainsPageAsync`: move `FromSqlInterpolated` cursor query from `GymTrainsController.BuildPageQuery`.
  - `GetReconstructionRecordsAsync`: move LINQ projection from `ReconstructionRunner.Run`.
  - `UpdateHappyBeforeTrainAsync`: `.Where(...).ExecuteUpdateAsync(...)` (EF bulk update, no per-row round-trip).
- `ImportRunRepository : IImportRunRepository`
- `ModifierProvenanceRepository : IModifierProvenanceRepository`
  - `StageReplacementForPlayerAsync`: `RemoveRange` existing + `AddRange` new — **no** `SaveChangesAsync`. Caller commits via `IUnitOfWork`.
- `AffiliationEventRepository : IAffiliationEventRepository`
- `LogTypeRepository : ILogTypeRepository`

No `BeginTransaction` calls — EF tracks changes on shared scoped context.

> **SaveChanges ownership**: Methods that stage changes for `ReconstructionRunner` (`StageReplacementForPlayerAsync`, `UpdateHappyBeforeTrainAsync`) do **not** call `SaveChangesAsync`. All other repository methods (import run lifecycle, affiliation events, log type upserts) self-commit since they are always single-operation calls. `ReconstructionRunner` calls `IUnitOfWork.SaveChangesAsync` once after all staging is done.

> **Migration/schema concern**: None. No entity changes, no new migrations.

---

## Phase 3 — Refactor Core Services

Three files lose `string databasePath`, gain injected repository dependencies.

### 3a — `src/HappyGymStats.Core/Fetch/LogFetcher.cs`

- **Remove**: `_databasePath` field, `DbContextOptionsBuilder`, `new HappyGymStatsDbContext(...)`, `db.Database.MigrateAsync()`, all `using HappyGymStats.Data.*`, `using Microsoft.EntityFrameworkCore`.
- **Constructor**: `LogFetcher(IUserLogEntryRepository userLogRepo, IImportRunRepository importRunRepo, TornApiClient client)`
- `MapUserLogEntry` private method is pure — keep unchanged.
- Import run lifecycle (create, update per page, complete/cancel/fail) goes through `IImportRunRepository`.

### 3b — `src/HappyGymStats.Core/Fetch/PerkLogFetcher.cs`

- **Remove**: `databasePath` parameter from `RunAsync`, all DbContext construction inside method.
- **Add to constructor**: `IAffiliationEventRepository`, `ILogTypeRepository`, `IUserLogEntryRepository`.
- `EnsureLogTypesRegisteredAsync` becomes call to `ILogTypeRepository.AddRangeIfMissingAsync`.

### 3c — `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`

- **Remove**: `_databasePath`, all DbContext construction, `db.Database.Migrate()`, `db.Database.BeginTransaction()`, `tx.Commit()`, all EF `using` imports.
- **Constructor**: `ReconstructionRunner(IUserLogEntryRepository userLogRepo, IImportRunRepository importRunRepo, IModifierProvenanceRepository provenanceRepo, IUnitOfWork unitOfWork)`
- `Run` becomes `async Task<RunResult>` (was sync due to sync EF calls).
- Provenance write-back: call `IModifierProvenanceRepository.StageReplacementForPlayerAsync` (stages, no commit).
- Happy updates: call `IUserLogEntryRepository.UpdateHappyBeforeTrainAsync` (stages tracked changes, no commit).
- After both staged: call `await _unitOfWork.SaveChangesAsync(ct)` — **one commit, atomicity preserved**.
- `BuildModifierProvenanceEntities` is pure — keep unchanged.

> **Note**: `UpdateHappyBeforeTrainAsync` must use **tracked EF updates** (load entities, mutate, let SaveChanges flush), NOT `ExecuteUpdateAsync`. `ExecuteUpdateAsync` bypasses the change tracker and commits immediately, breaking atomicity with the staged provenance changes. The performance trade-off is acceptable for this workload (personal stats tracker, not bulk-ETL).

---

## Phase 4 — Move `ImportService` → Core

**Move**: `src/HappyGymStats.Api/ImportService.cs` → `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`

- Rename class `ImportService` → `ImportOrchestrator`, namespace `HappyGymStats.Core.Import`.
- Move `ImportJobStatus` and `ImportJobRequest` records into same file.
- **Remove**: `string _databasePath`, all `new LogFetcher(...)`, `new PerkLogFetcher(...)`, `new ReconstructionRunner(...)`.
- **Constructor**: `ImportOrchestrator(IServiceScopeFactory scopeFactory, SurfacesCacheWriter surfacesCacheWriter, ILogger<ImportOrchestrator> logger)`
  - `LogFetcher`, `PerkLogFetcher`, `ReconstructionRunner` are **scoped** — injecting them directly into a singleton causes captive dependency (DbContext concurrency crashes, memory leaks).
  - Instead, inside the Execute/Run method body: `using var scope = _scopeFactory.CreateScope(); var logFetcher = scope.ServiceProvider.GetRequiredService<LogFetcher>(); ...`
  - All scoped work happens within the scope's lifetime; scope disposed when import run completes.
  - `SurfacesCacheWriter` is already singleton — safe to inject directly.
- **Delete**: `src/HappyGymStats.Api/ImportService.cs`

---

## Phase 5 — Move `SurfacesCacheWriter` → Core

**Move**: `src/HappyGymStats.Api/SurfacesCacheWriter.cs` → `src/HappyGymStats.Core/Surfaces/SurfacesCacheWriter.cs`

- Namespace: `HappyGymStats.Core.Surfaces`.
- Replace DbContext resolution via `IServiceScopeFactory` with `IUserLogEntryRepository` and `IModifierProvenanceRepository` resolved from scope. Class stays singleton.
- `ProvenanceWarning`, `WarningProjection` private records, and all static helpers stay in file.
- **Delete**: `src/HappyGymStats.Api/SurfacesCacheWriter.cs`

---

## Phase 6 — Fix `GymTrainsController`

**New file**: `src/HappyGymStats.Core/Services/GymTrainsService.cs`
```csharp
public sealed class GymTrainsService(IUserLogEntryRepository repo)
{
    public Task<CursorPage<GymTrainDto>> GetPageAsync(int take, PageCursor? cursor, CancellationToken ct)
        => repo.GetGymTrainsPageAsync(take, cursor, ct);
}
```

**`GymTrainsController`** (`src/HappyGymStats.Api/Controllers/GymTrainsController.cs`):
- Replace `HappyGymStatsDbContext _db` with `GymTrainsService _service`.
- Remove `BuildPageQuery` method entirely.
- Remove all `using HappyGymStats.Data.*` and `using Microsoft.EntityFrameworkCore`.
- `CursorEncoder.TryDecode` stays (Api infrastructure concern).

---

## Phase 7 — Update `Api/Program.cs`

1. Register typed HTTP client for `TornApiClient`:
   ```csharp
   builder.Services.AddHttpClient<TornApiClient>(client =>
   {
       client.BaseAddress = new Uri("https://api.torn.com/");
   });
   ```
   This makes `TornApiClient` injectable as a scoped service with its `HttpClient` lifecycle managed by `IHttpClientFactory`. Remove any manual `new HttpClient()` / `IHttpClientFactory` usage in `ImportOrchestrator`.
2. Register `IUnitOfWork`:
   ```csharp
   builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
   ```
3. Register scoped repositories:
   ```csharp
   builder.Services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
   builder.Services.AddScoped<IImportRunRepository, ImportRunRepository>();
   builder.Services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
   builder.Services.AddScoped<IAffiliationEventRepository, AffiliationEventRepository>();
   builder.Services.AddScoped<ILogTypeRepository, LogTypeRepository>();
   ```
3. Register scoped Core services:
   ```csharp
   builder.Services.AddScoped<LogFetcher>();
   builder.Services.AddScoped<PerkLogFetcher>();
   builder.Services.AddScoped<ReconstructionRunner>();
   builder.Services.AddScoped<GymTrainsService>();
   ```
4. Update singleton registrations:
   ```csharp
   builder.Services.AddSingleton<SurfacesCacheWriter>(...); // Core namespace now
   builder.Services.AddSingleton<ImportOrchestrator>(...);  // Core namespace now
   builder.Services.AddHostedService(sp => sp.GetRequiredService<ImportOrchestrator>());
   ```
5. Remove `databasePath` pass-through (no longer needed).
6. Swap `using HappyGymStats.Api` → `using HappyGymStats.Core.Import` + `using HappyGymStats.Core.Surfaces`.

**`ImportController`**: swap injection type `ImportService` → `ImportOrchestrator`.

---

## Phase 8 — Update `HappyGymStats.Cli`

**`src/HappyGymStats.Cli/Program.cs`**: add DI setup block:
```csharp
var services = new ServiceCollection();
services.AddDbContext<HappyGymStatsDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
services.AddScoped<IImportRunRepository, ImportRunRepository>();
services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
services.AddScoped<IAffiliationEventRepository, AffiliationEventRepository>();
services.AddScoped<ILogTypeRepository, LogTypeRepository>();
services.AddScoped<LogFetcher>();
services.AddScoped<PerkLogFetcher>();
services.AddScoped<ReconstructionRunner>();
services.AddHttpClient<TornApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.torn.com/");
});
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
var sp = services.BuildServiceProvider();
```

Replace all `new LogFetcher(databasePath, ...)` / `new ReconstructionRunner(databasePath)` calls with `sp.GetRequiredService<...>()` or scope resolutions.

**`src/HappyGymStats.Cli/HappyGymStats.Cli.csproj`**: remove direct `<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />` (flows transitively via Data).

---

## Phase 9 — Update Tests

**`tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`**:
- Build `ServiceCollection` in test setup: in-memory SQLite `HappyGymStatsDbContext`, all repositories, `LogFetcher`.
- Replace `new LogFetcher(dbPath, tornClient)` with DI-resolved instance.

**`tests/HappyGymStats.Tests/ApiEndpointTests.cs`** (via `TestApplicationFactory`):
- `ConfigureWebHost` must register repository implementations on top of existing in-memory SQLite `DbContext` override.
- `SeedUserLogEntriesAsync` writes directly to `HappyGymStatsDbContext` — unchanged, fine for tests.

---

## Files Summary

| Action | File |
|---|---|
| Create (5+1) | `Core/Repositories/I*Repository.cs` + `IUnitOfWork.cs` |
| Create (5) | `Data/Repositories/*Repository.cs` |
| Create | `Core/Models/GymTrainDto.cs`, `CursorPage.cs`, `PageCursor.cs` |
| Create | `Core/Services/GymTrainsService.cs` |
| Move+refactor | `Api/ImportService.cs` → `Core/Import/ImportOrchestrator.cs` |
| Move+refactor | `Api/SurfacesCacheWriter.cs` → `Core/Surfaces/SurfacesCacheWriter.cs` |
| Refactor | `Core/Fetch/LogFetcher.cs` |
| Refactor | `Core/Fetch/PerkLogFetcher.cs` |
| Refactor | `Core/Reconstruction/ReconstructionRunner.cs` |
| Refactor | `Api/Controllers/GymTrainsController.cs` |
| Refactor | `Api/Controllers/ImportController.cs` (type swap only) |
| Refactor | `Api/Program.cs` |
| Refactor | `Cli/Program.cs` |
| Refactor | `Cli/HappyGymStats.Cli.csproj` (remove EF Sqlite ref) |
| Refactor | `Tests/DbPipelineIntegrationTests.cs` |
| Refactor | `Tests/ApiEndpointTests.cs` |
| Delete | `Api/Models/GymTrainDto.cs`, `CursorPage.cs`, `PageCursor.cs` |

---

## Verification

1. `dotnet build HappyGymStats.sln` — zero errors, zero warnings about missing references.
2. `dotnet test tests/HappyGymStats.Tests` — all existing tests pass.
3. `dotnet run --project src/HappyGymStats.Api` — API starts, `GET /api/v1/torn/gym-trains` returns `200` (or empty `CursorPage`), `GET /api/v1/torn/health` returns `200`.
4. `dotnet run --project src/HappyGymStats.Cli` — interactive menu appears without DI errors.
5. No file in `HappyGymStats.Core` contains `using Microsoft.EntityFrameworkCore`.
6. No file in `HappyGymStats.Api` contains `using HappyGymStats.Data.Entities`.
