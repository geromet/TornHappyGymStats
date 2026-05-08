# DB Schema Proposal

## Goals
- Multi-player: `PlayerId` (Torn player int) on every table; no dedicated Players table
- Fix `LogId` uniqueness — Torn IDs are per-player, not global
- Fix `AffiliationEvents` missing `PlayerId`
- Fix `ModifierProvenance` FactionId/CompanyId null; add PlayerId; FK → UserLogEntries
- ~78% storage reduction: drop `RawJson`, store only `DataJson` (`data` object, ~40 bytes avg)
- Drop `DerivedHappyEvents` — redundant after reconstruction; if affiliation data is missing, the happy timeline is unreliable anyway
- Drop `DerivedGymTrains` — `HappyBeforeTrain` moves to `UserLogEntries` as a nullable column
- No surrogate `Id` column where a natural composite key exists
- `ModifierProvenance.Scope` → INTEGER bitmask (1=personal, 2=faction, 4=company)
- Drop `VerificationReasonCode` + `VerificationDetails` from `ModifierProvenance` — scope + ids are sufficient
- Scale target: 100K players × 10K logs = ~1B rows

---

## Storage estimate

| Column    | Current | Proposed |
|-----------|---------|----------|
| RawJson   | ~500 B  | dropped  |
| Typed columns | —  | ~100 B avg (14 nullable numerics; NULLs are free in SQLite) |
| Title     | ~30 B   | dropped (code constant) |
| Category  | ~15 B   | dropped (code constant) |
| **Total** | ~600 B  | ~145 B   |

~1B rows: **~120 GB** proposed vs ~560 GB current.

---

## Decisions

1. `GET /v2/user/basic` called every import run — API key can map to different players.
2. JSONL dropped entirely. `UserLogEntries` typed columns are sole source of truth. Raw JSON is discarded immediately after parsing — never persisted.
3. Typed columns kept indefinitely; re-reconstruction reads from `UserLogEntries` without re-fetching from the API.
4. `MaxHappyAtTimeUtc` → `MaxHappy` everywhere (it is a happy value, not a timestamp).
5. Fresh migration — drop all tables, recreate. Data re-fetched from Torn API.

---

## Tables

### `UserLogEntries`

| Column             | Type              | Notes |
|--------------------|-------------------|-------|
| PlayerId           | INTEGER           | Torn player ID |
| LogEntryId         | TEXT              | Torn opaque string ID |
| OccurredAtUtc      | DATETIME NOT NULL | |
| LogTypeId          | INTEGER NOT NULL  | `details.id` |
| HappyBeforeApi     | INTEGER NULL      | `data.happy_before` from Torn API; present on recent logs only; 100% accurate |
| HappyBeforeTrain   | INTEGER NULL      | Reconstruction-derived fallback; use `COALESCE(HappyBeforeApi, HappyBeforeTrain)` |
| HappyBeforeDelta   | INTEGER NULL      | `HappyBeforeApi - HappyBeforeTrain`; NULL if either is absent; non-zero indicates reconstruction error |
| HappyUsed          | INTEGER NULL      | `data.happy_used` |
| EnergyUsed         | REAL NULL         | `data.energy_used` |
| StrengthBefore     | REAL NULL         | `data.strength_before` |
| StrengthIncreased  | REAL NULL         | `data.strength_increased` |
| DefenseBefore      | REAL NULL         | `data.defense_before` |
| DefenseIncreased   | REAL NULL         | `data.defense_increased` |
| SpeedBefore        | REAL NULL         | `data.speed_before` |
| SpeedIncreased     | REAL NULL         | `data.speed_increased` |
| DexterityBefore    | REAL NULL         | `data.dexterity_before` |
| DexterityIncreased | REAL NULL         | `data.dexterity_increased` |
| MaxHappyBefore     | INTEGER NULL      | `data.maximum_happy_before` |
| MaxHappyAfter      | INTEGER NULL      | `data.maximum_happy_after` |

**PK:** `(PlayerId, LogEntryId)`
**Indexes:** `(PlayerId, OccurredAtUtc)`, `(PlayerId, LogTypeId)`

**Dropped:** `DataJson` — all gym train fields promoted to typed columns; perk log entries have all data columns NULL

---

### `DerivedGymTrains` — **DROPPED**

`HappyBeforeTrain` moves to `UserLogEntries` as a nullable column. Everything else (`OccurredAtUtc`, `HappyUsed`, `HappyAfterTrain`) is read directly from `UserLogEntries` columns.

---

### `DerivedHappyEvents` — **DROPPED**

Redundant after reconstruction. If `AffiliationEvents` reveals a gap in faction/company data, the timeline is unreliable for that player regardless. The `/happy-events` API endpoint will be removed or rebuilt as a live query over `UserLogEntries`.

---

### `AffiliationEvents`

`OccurredAtUtc` available via JOIN to `UserLogEntries` on `(PlayerId, SourceLogEntryId)`.

| Column           | Type             | Notes |
|------------------|------------------|-------|
| PlayerId         | INTEGER          | |
| SourceLogEntryId | TEXT             | |
| LogTypeId        | INTEGER NOT NULL | |
| Scope            | INTEGER NOT NULL | 2=faction, 4=company (aligns with ModifierProvenance bitmask) |
| AffiliationId    | INTEGER NOT NULL | Torn faction/company ID |
| SenderId         | INTEGER NULL     | |
| PositionBefore   | INTEGER NULL     | |
| PositionAfter    | INTEGER NULL     | |

**PK:** `(PlayerId, SourceLogEntryId)`
**Index:** `(PlayerId, Scope, AffiliationId)`

**Dropped:** `Id` autoincrement, `OccurredAtUtc`, `Title`
**Changed:** `Scope` INTEGER (was TEXT)

---

### `ModifierProvenance`

`FactionId`/`CompanyId` populated by querying `AffiliationEvents` for the most recent entry where `UserLogEntries.OccurredAtUtc <= gym train timestamp` and matching scope.

| Column             | Type              | Notes |
|--------------------|-------------------|-------|
| PlayerId           | INTEGER           | |
| LogEntryId         | TEXT              | → UserLogEntries |
| Scope              | INTEGER NOT NULL  | Bitmask: 1=personal, 2=faction, 4=company; combinations: 3, 5, 6, 7 |
| SubjectId          | INTEGER NULL      | Player ID (personal scope) |
| FactionId          | INTEGER NULL      | faction scope |
| CompanyId          | INTEGER NULL      | company scope |
| VerificationStatus | INTEGER NOT NULL  | 1=verified, 2=unresolved, 3=unavailable |

**PK:** `(PlayerId, LogEntryId, Scope)`
**Index:** `(PlayerId, VerificationStatus)`

**Dropped:** `Id` autoincrement, `DerivedGymTrainLogEntryId` (renamed to `LogEntryId` → `UserLogEntries`), `ValidFromUtc`, `ValidToUtc`, `VerificationReasonCode`, `VerificationDetails`
**Changed:** `Scope` INTEGER bitmask (was TEXT); `SubjectId`/`FactionId`/`CompanyId` INTEGER (was TEXT); `VerificationStatus` INTEGER (was TEXT)

---

### `ImportCheckpoints` — **MERGED INTO `ImportRuns`**

`NextUrl` moves to `ImportRuns`; latest run row per player serves as the checkpoint; cumulative totals are `SUM()` over `ImportRuns`.

---

### `ImportRuns`

One row per run per player. The latest row per player serves as the import checkpoint — no separate checkpoint table needed. Cumulative totals are `SUM(LogsFetched)` / `SUM(LogsAppended)` over all rows for a player.

| Column         | Type                     | Notes |
|----------------|--------------------------|-------|
| Id             | INTEGER PK autoincrement | |
| PlayerId       | INTEGER NOT NULL         | |
| StartedAtUtc   | DATETIME NOT NULL        | |
| CompletedAtUtc | DATETIME NULL            | |
| Outcome        | TEXT NULL                | NULL while in progress |
| ErrorMessage   | TEXT NULL                | |
| PagesFetched   | INTEGER                  | |
| LogsFetched    | INTEGER                  | |
| LogsAppended   | INTEGER                  | |
| NextUrl        | TEXT NULL                | Pagination cursor; set while run is in progress or paused |

**Index:** `(PlayerId, StartedAtUtc)`

**Absorbed from ImportCheckpoints:** `NextUrl`, `LastRunStartedAt`, `LastRunCompletedAt`, `LastRunOutcome`, `LastErrorMessage`, `LastErrorAt`, `TotalFetchedCount`, `TotalAppendedCount`

---

### `LogTypes`

Reference table mapping Torn log type IDs to their human-readable titles.

| Column       | Type             | Notes |
|--------------|------------------|-------|
| LogTypeId    | INTEGER PK       | Torn log type ID |
| LogTypeTitle | TEXT NOT NULL    | Human-readable title |

**Dropped:** `PlayerId`, `FetchScope`, `Status`, `NextUrl`, `TotalFetched`, `CompletedAtUtc`, `LastAttemptedAtUtc` — per-player fetch state removed; `UserLogEntries` is source of truth

---

## API surface impact

| Endpoint | Change |
|----------|--------|
| `POST /api/v1/torn/import-jobs` | Resolves PlayerId from API key before fetch |
| `GET /api/v1/torn/gym-trains` | Reads `UserLogEntries` directly; `HappyBeforeTrain` is a column there |
| `GET /api/v1/torn/happy-events` | **Removed or rebuilt** as live query over `UserLogEntries` |
