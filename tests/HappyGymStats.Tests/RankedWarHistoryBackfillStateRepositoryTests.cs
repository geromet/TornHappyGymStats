using HappyGymStats.Core.Repositories;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class RankedWarHistoryBackfillStateRepositoryTests
{
    private const string ScopeKey = "public-war";

    [Fact]
    public async Task UpsertAsync_creates_a_new_scope_state_and_reloads_it_from_a_fresh_context()
    {
        await using var scope = await TestScope.CreateAsync();

        var createdAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        await scope.Repository.UpsertAsync(new RankedWarHistoryBackfillStateEntity
        {
            ScopeKey = ScopeKey,
            Status = "Running",
            Phase = "FetchingHistoryPage",
            NextHistoryPageUrl = null,
            PagesProcessed = 0,
            ReportsProcessed = 0,
            RetryCount = 0,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        }, CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        await using var reloadDb = scope.CreateFreshContext();
        var reloadRepository = new RankedWarHistoryBackfillStateRepository(reloadDb);
        var stored = await reloadRepository.GetAsync(ScopeKey, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("Running", stored.Status);
        Assert.Equal("FetchingHistoryPage", stored.Phase);
        Assert.Equal(createdAt.ToUniversalTime(), stored.CreatedAtUtc);
        Assert.Null(stored.NextHistoryPageUrl);
    }

    [Fact]
    public async Task UpsertAsync_advances_cursor_and_progress_counters_idempotently_by_scope_key()
    {
        await using var scope = await TestScope.CreateAsync();

        await scope.Repository.UpsertAsync(CreateState(status: "Running", pagesProcessed: 0, reportsProcessed: 0), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        await scope.Repository.UpsertAsync(CreateState(
            status: "Running",
            nextHistoryPageUrl: "https://api.torn.com/v2/faction/warfareranked?cursor=abc",
            lastProcessedWarId: 48377,
            pagesProcessed: 1,
            reportsProcessed: 3), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        Assert.Equal(1, await scope.Db.RankedWarHistoryBackfillState.CountAsync());

        var stored = await scope.Repository.GetAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("https://api.torn.com/v2/faction/warfareranked?cursor=abc", stored.NextHistoryPageUrl);
        Assert.Equal(48377, stored.LastProcessedWarId);
        Assert.Equal(1, stored.PagesProcessed);
        Assert.Equal(3, stored.ReportsProcessed);
    }

    [Fact]
    public async Task UpsertAsync_records_a_retryable_failure_and_then_clears_it_after_success()
    {
        await using var scope = await TestScope.CreateAsync();

        await scope.Repository.UpsertAsync(CreateState(status: "Running"), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        var failedAt = new DateTimeOffset(2026, 9, 2, 11, 0, 0, TimeSpan.Zero);
        await scope.Repository.UpsertAsync(CreateState(
            status: "WaitingRetry",
            retryCount: 1,
            lastFailureCategory: "RateLimited",
            lastErrorMessage: "Torn API error 5: rate limit hit.",
            lastFailureAtUtc: failedAt,
            nextRetryAtUtc: failedAt.AddSeconds(60)), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        var failing = await scope.Repository.GetAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(failing);
        Assert.Equal("WaitingRetry", failing.Status);
        Assert.Equal("RateLimited", failing.LastFailureCategory);
        Assert.Equal(1, failing.RetryCount);
        Assert.NotNull(failing.NextRetryAtUtc);

        var succeededAt = failedAt.AddMinutes(2);
        await scope.Repository.UpsertAsync(CreateState(
            status: "Running",
            retryCount: 0,
            lastFailureCategory: null,
            lastErrorMessage: null,
            lastFailureAtUtc: failing.LastFailureAtUtc,
            nextRetryAtUtc: null,
            lastSuccessAtUtc: succeededAt), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        var recovered = await scope.Repository.GetAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal("Running", recovered.Status);
        Assert.Null(recovered.LastFailureCategory);
        Assert.Null(recovered.LastErrorMessage);
        Assert.Null(recovered.NextRetryAtUtc);
        Assert.Equal(0, recovered.RetryCount);
        Assert.Equal(succeededAt.ToUniversalTime(), recovered.LastSuccessAtUtc);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_scope()
    {
        await using var scope = await TestScope.CreateAsync();

        var stored = await scope.Repository.GetAsync("unknown-scope", CancellationToken.None);

        Assert.Null(stored);
    }

    private static RankedWarHistoryBackfillStateEntity CreateState(
        string status = "Running",
        string? phase = "FetchingHistoryPage",
        string? nextHistoryPageUrl = null,
        long? lastProcessedWarId = null,
        long pagesProcessed = 0,
        long reportsProcessed = 0,
        int retryCount = 0,
        string? lastFailureCategory = null,
        string? lastErrorMessage = null,
        DateTimeOffset? lastSuccessAtUtc = null,
        DateTimeOffset? lastFailureAtUtc = null,
        DateTimeOffset? nextRetryAtUtc = null)
        => new()
        {
            ScopeKey = ScopeKey,
            Status = status,
            Phase = phase,
            NextHistoryPageUrl = nextHistoryPageUrl,
            LastProcessedWarId = lastProcessedWarId,
            PagesProcessed = pagesProcessed,
            ReportsProcessed = reportsProcessed,
            RetryCount = retryCount,
            LastFailureCategory = lastFailureCategory,
            LastErrorMessage = lastErrorMessage,
            LastSuccessAtUtc = lastSuccessAtUtc,
            LastFailureAtUtc = lastFailureAtUtc,
            NextRetryAtUtc = nextRetryAtUtc,
            CreatedAtUtc = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
        };

    private sealed class TestScope : IAsyncDisposable
    {
        private TestScope(SqliteConnection connection, HappyGymStatsDbContext db, IRankedWarHistoryBackfillStateRepository repository)
        {
            Connection = connection;
            Db = db;
            Repository = repository;
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext Db { get; }
        public IRankedWarHistoryBackfillStateRepository Repository { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var db = new HappyGymStatsDbContext(CreateOptions(connection));
            await db.Database.EnsureCreatedAsync();

            return new TestScope(connection, db, new RankedWarHistoryBackfillStateRepository(db));
        }

        public HappyGymStatsDbContext CreateFreshContext()
            => new(CreateOptions(Connection));

        private static DbContextOptions<HappyGymStatsDbContext> CreateOptions(SqliteConnection connection)
            => new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
