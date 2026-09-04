using HappyGymStats.Core.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ImportAdmissionIsolationTests
{
    [Fact]
    public void Busy_enqueue_does_not_return_active_job_identity_or_status()
    {
        var orchestrator = new ImportOrchestrator(
            scopeFactory: null!,
            surfacesCacheWriter: null!,
            NullLogger<ImportOrchestrator>.Instance);

        var ownerAnonymousId = Guid.NewGuid();
        var active = orchestrator.EnqueueForAnonymousId("owner-key", ownerAnonymousId, fresh: true);
        var busy = orchestrator.Enqueue("other-key", fresh: true);

        Assert.Equal("queued", active.Outcome);
        Assert.Equal(ownerAnonymousId, active.AnonymousId);

        Assert.Equal("busy", busy.Outcome);
        Assert.Equal(string.Empty, busy.Id);
        Assert.Equal(Guid.Empty, busy.AnonymousId);
        Assert.Equal(DateTimeOffset.UnixEpoch, busy.StartedAtUtc);
        Assert.Null(busy.CompletedAtUtc);
        Assert.Null(busy.ErrorMessage);
        Assert.Equal(0, busy.PagesFetched);
        Assert.Equal(0, busy.LogsFetched);
        Assert.Equal(0, busy.LogsAppended);

        Assert.Same(active, orchestrator.Latest);
    }

    [Fact]
    public async Task Concurrent_admission_exposes_only_one_real_job()
    {
        var orchestrator = new ImportOrchestrator(
            scopeFactory: null!,
            surfacesCacheWriter: null!,
            NullLogger<ImportOrchestrator>.Instance);

        var attempts = Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => orchestrator.Enqueue($"key-{index}", fresh: true)))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        var admitted = Assert.Single(results.Where(result => result.Outcome == "queued"));
        Assert.NotEqual(Guid.Empty, admitted.AnonymousId);
        Assert.False(string.IsNullOrWhiteSpace(admitted.Id));

        var busy = results.Where(result => result.Outcome == "busy").ToArray();
        Assert.Equal(15, busy.Length);
        Assert.All(busy, result =>
        {
            Assert.Equal(string.Empty, result.Id);
            Assert.Equal(Guid.Empty, result.AnonymousId);
            Assert.Null(result.ErrorMessage);
        });
    }
}
