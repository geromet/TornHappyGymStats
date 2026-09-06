using System.Net;
using System.Security.Cryptography;
using System.Text;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data.Entities;
using HappyGymStats.Encryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class AnonymousImportWorkerIdentityTests
{
    private const int TornPlayerId = 24680;

    [Fact]
    public async Task Keyed_worker_initialization_commits_only_encrypted_player_identity()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new RecordingUnitOfWork();
        await using var harness = CreateHarness(repository, unitOfWork);

        await harness.Orchestrator.StartAsync(CancellationToken.None);
        var admitted = harness.Orchestrator.Enqueue(
            "fixture-api-key",
            fresh: true,
            recipient.ExportSubjectPublicKeyInfo());

        await WaitForTerminalAsync(harness.Orchestrator, admitted.Id);

        Assert.Equal(admitted.AnonymousId, repository.StoredFor);
        Assert.NotNull(repository.Ciphertext);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.False(Encoding.UTF8.GetBytes(TornPlayerId.ToString()).SequenceEqual(repository.Ciphertext!));
        Assert.Equal(
            TornPlayerId.ToString(),
            Encoding.UTF8.GetString(Ecies.Decrypt(recipient, repository.Ciphertext!)));
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Fact]
    public async Task Worker_without_public_key_never_stages_or_commits_identity()
    {
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new RecordingUnitOfWork();
        await using var harness = CreateHarness(repository, unitOfWork);

        await harness.Orchestrator.StartAsync(CancellationToken.None);
        var admitted = harness.Orchestrator.Enqueue("fixture-api-key", fresh: true);

        await WaitForTerminalAsync(harness.Orchestrator, admitted.Id);

        Assert.Null(repository.StoredFor);
        Assert.Null(repository.Ciphertext);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Fact]
    public async Task Identity_repository_failure_never_attempts_commit_or_downstream_fetch()
    {
        var repository = new RecordingIdentityMapRepository(throwOnStore: true);
        var unitOfWork = new RecordingUnitOfWork();
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        await using var harness = CreateHarness(repository, unitOfWork);

        await harness.Orchestrator.StartAsync(CancellationToken.None);
        var admitted = harness.Orchestrator.Enqueue(
            "fixture-api-key",
            fresh: true,
            recipient.ExportSubjectPublicKeyInfo());

        var terminal = await WaitForTerminalAsync(harness.Orchestrator, admitted.Id);

        Assert.Equal("failed", terminal.Outcome);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Fact]
    public async Task Identity_commit_failure_never_reaches_downstream_fetch()
    {
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new ThrowingUnitOfWork();
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        await using var harness = CreateHarness(repository, unitOfWork);

        await harness.Orchestrator.StartAsync(CancellationToken.None);
        var admitted = harness.Orchestrator.Enqueue(
            "fixture-api-key",
            fresh: true,
            recipient.ExportSubjectPublicKeyInfo());

        var terminal = await WaitForTerminalAsync(harness.Orchestrator, admitted.Id);

        Assert.Equal("failed", terminal.Outcome);
        Assert.NotNull(repository.Ciphertext);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.Equal(1, harness.Handler.Requests);
    }

    private static WorkerHarness CreateHarness(
        IIdentityMapRepository repository,
        IUnitOfWork unitOfWork)
    {
        var handler = new TornIdentityHandler();
        var services = new ServiceCollection();
        services.AddSingleton(new TornApiClient(new HttpClient(handler)));
        services.AddSingleton(repository);
        services.AddSingleton(unitOfWork);
        var provider = services.BuildServiceProvider();
        var orchestrator = new ImportOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            surfacesCacheWriter: null!,
            NullLogger<ImportOrchestrator>.Instance);
        return new WorkerHarness(provider, orchestrator, handler);
    }

    private static async Task<ImportJobStatus> WaitForTerminalAsync(
        ImportOrchestrator orchestrator,
        string jobId)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (orchestrator.Latest is { IsTerminal: true } latest
                && string.Equals(latest.Id, jobId, StringComparison.Ordinal))
            {
                return latest;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The import worker did not reach a terminal state.");
    }

    private sealed class WorkerHarness(
        ServiceProvider provider,
        ImportOrchestrator orchestrator,
        TornIdentityHandler handler) : IAsyncDisposable
    {
        public ImportOrchestrator Orchestrator { get; } = orchestrator;
        public TornIdentityHandler Handler { get; } = handler;

        public async ValueTask DisposeAsync()
        {
            await Orchestrator.StopAsync(CancellationToken.None);
            Orchestrator.Dispose();
            await provider.DisposeAsync();
            Handler.Dispose();
        }
    }

    private sealed class TornIdentityHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("api.torn.com", request.RequestUri?.Host);
            Assert.Equal("/v2/user/basic", request.RequestUri?.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"player_id\":{TornPlayerId}}}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class RecordingIdentityMapRepository(bool throwOnStore = false) : IIdentityMapRepository
    {
        public Guid? StoredFor { get; private set; }
        public byte[]? Ciphertext { get; private set; }

        public Task StoreEncryptedTornPlayerIdAsync(
            Guid anonymousId,
            byte[] encryptedTornPlayerId,
            CancellationToken ct)
        {
            if (throwOnStore)
                throw new InvalidOperationException("identity persistence failed");

            StoredFor = anonymousId;
            Ciphertext = encryptedTornPlayerId;
            return Task.CompletedTask;
        }

        public Task CreateAsync(IdentityMapEntity entity, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct) => throw new NotSupportedException();
        public Task StorePublicKeyAsync(Guid anonymousId, byte[] publicKeySpki, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            throw new InvalidOperationException("identity commit failed");
        }
    }
}
