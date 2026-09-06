using HappyGymStats.Api.Controllers;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Provisional;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class AnonymousImportInitializationTests
{
    [Fact]
    public async Task Identity_commit_completes_before_reserved_work_is_published()
    {
        var orchestrator = CreateOrchestrator();
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new BlockingUnitOfWork();
        var controller = CreateController(orchestrator, repository, unitOfWork, new StubTokenService());

        var actionTask = controller.StartAnonymousImport(
            new ImportRequest("not-a-live-key", Fresh: true, PublicKey: null),
            CancellationToken.None);

        await unitOfWork.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(orchestrator.Latest);
        Assert.Equal("initializing", orchestrator.Latest!.Outcome);
        Assert.Equal(orchestrator.Latest.AnonymousId, repository.Created!.AnonymousId);

        var competing = orchestrator.Enqueue("competing-key", fresh: true);
        Assert.Equal("busy", competing.Outcome);
        Assert.Equal(Guid.Empty, competing.AnonymousId);
        Assert.Equal(string.Empty, competing.Id);

        unitOfWork.AllowSave.TrySetResult(true);
        var result = Assert.IsType<ObjectResult>(await actionTask);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.True(unitOfWork.Saved);
        Assert.Equal("queued", orchestrator.Latest!.Outcome);
    }

    [Fact]
    public async Task Token_failure_publishes_nothing_and_releases_admission_capacity()
    {
        var orchestrator = CreateOrchestrator();
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var controller = CreateController(orchestrator, repository, unitOfWork, new ThrowingTokenService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAnonymousImport(
            new ImportRequest("must-not-run", Fresh: true, PublicKey: null),
            CancellationToken.None));

        Assert.Null(repository.Created);
        Assert.False(unitOfWork.Saved);
        Assert.Null(orchestrator.Latest);

        var replacement = orchestrator.Enqueue("replacement-key", fresh: true);
        Assert.Equal("queued", replacement.Outcome);
    }

    [Fact]
    public async Task Persistence_failure_publishes_nothing_and_releases_admission_capacity()
    {
        var orchestrator = CreateOrchestrator();
        var repository = new RecordingIdentityMapRepository();
        var unitOfWork = new ThrowingUnitOfWork();
        var controller = CreateController(orchestrator, repository, unitOfWork, new StubTokenService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAnonymousImport(
            new ImportRequest("must-not-run", Fresh: true, PublicKey: null),
            CancellationToken.None));

        Assert.NotNull(repository.Created);
        Assert.Null(orchestrator.Latest);

        var replacement = orchestrator.Enqueue("replacement-key", fresh: true);
        Assert.Equal("queued", replacement.Outcome);
    }

    private static ImportController CreateController(
        ImportOrchestrator orchestrator,
        IIdentityMapRepository repository,
        IUnitOfWork unitOfWork,
        IProvisionalTokenService tokenService)
        => new(
            orchestrator,
            repository,
            unitOfWork,
            tokenService,
            NullLogger<ImportController>.Instance);

    private static ImportOrchestrator CreateOrchestrator()
        => new(
            scopeFactory: null!,
            surfacesCacheWriter: null!,
            NullLogger<ImportOrchestrator>.Instance);

    private sealed class StubTokenService : IProvisionalTokenService
    {
        public string Issue(Guid anonymousId) => $"token-for-{anonymousId}";
        public Guid? Validate(string token) => throw new NotSupportedException();
    }

    private sealed class ThrowingTokenService : IProvisionalTokenService
    {
        public string Issue(Guid anonymousId) => throw new InvalidOperationException("invalid token configuration");
        public Guid? Validate(string token) => throw new NotSupportedException();
    }

    private sealed class RecordingIdentityMapRepository : IIdentityMapRepository
    {
        public IdentityMapEntity? Created { get; private set; }

        public Task CreateAsync(IdentityMapEntity entity, CancellationToken ct)
        {
            Created = entity;
            return Task.CompletedTask;
        }

        public Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct)
            => throw new NotSupportedException();

        public Task StoreEncryptedTornPlayerIdAsync(Guid anonymousId, byte[] encryptedTornPlayerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task StorePublicKeyAsync(Guid anonymousId, byte[] publicKeySpki, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class BlockingUnitOfWork : IUnitOfWork
    {
        public TaskCompletionSource<bool> SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Saved { get; private set; }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveEntered.TrySetResult(true);
            await AllowSave.Task.WaitAsync(cancellationToken);
            Saved = true;
            return 1;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public bool Saved { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saved = true;
            return Task.FromResult(1);
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("persistence failed");
    }
}
