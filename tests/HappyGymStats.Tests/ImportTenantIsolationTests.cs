using HappyGymStats.Core.Import;
using HappyGymStats.Core.Surfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ImportTenantIsolationTests
{
    [Fact]
    public void Busy_admission_returns_no_active_tenant_status()
    {
        var orchestrator = CreateOrchestrator();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();

        var first = orchestrator.TryEnqueueForAnonymousId("key-a", ownerA, fresh: false);
        var second = orchestrator.TryEnqueueForAnonymousId("key-b", ownerB, fresh: false);

        Assert.True(first.Accepted);
        Assert.NotNull(first.Status);
        Assert.Equal(ownerA, first.Status!.AnonymousId);

        Assert.False(second.Accepted);
        Assert.Null(second.Status);
        Assert.Null(orchestrator.GetLatestForAnonymousId(ownerB));
        Assert.Equal(first.Status.Id, orchestrator.GetLatestForAnonymousId(ownerA)?.Id);
    }

    [Fact]
    public void Status_capability_only_resolves_the_owner_it_was_issued_for()
    {
        var orchestrator = CreateOrchestrator();
        var owner = Guid.NewGuid();
        var accepted = orchestrator.TryEnqueueForAnonymousId("key", owner, fresh: true);
        Assert.True(accepted.Accepted);

        var capability = orchestrator.IssueStatusCapability(owner);

        Assert.Equal(accepted.Status?.Id, orchestrator.GetLatestForCapability(capability)?.Id);
        Assert.Null(orchestrator.GetLatestForCapability(Convert.ToHexString(Guid.NewGuid().ToByteArray())));
        Assert.Null(orchestrator.GetLatestForAnonymousId(Guid.NewGuid()));
    }

    [Fact]
    public void Owner_scoped_enqueue_rejects_empty_owner_instead_of_resolving_global_recency()
    {
        var orchestrator = CreateOrchestrator();

        Assert.Throws<ArgumentException>(() =>
            orchestrator.TryEnqueueForAnonymousId("key", Guid.Empty, fresh: false));
    }

    private static ImportOrchestrator CreateOrchestrator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var writer = new SurfacesCacheWriter(scopeFactory, Path.GetTempPath());
        var logger = provider.GetRequiredService<ILogger<ImportOrchestrator>>();
        return new ImportOrchestrator(scopeFactory, writer, logger);
    }
}
