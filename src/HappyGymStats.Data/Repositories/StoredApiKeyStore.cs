using System.Data;
using HappyGymStats.Contracts.Compliance;
using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public enum StoredApiKeyWriteStatus
{
    Stored = 0,
    OwnerNotFound = 1,
    ConsentRequired = 2,
}

public enum StoredApiKeyConnectionStatus
{
    Connected = 0,
    NotConnected = 1,
    OwnerNotFound = 2,
}

public enum StoredApiKeyRevokeStatus
{
    Revoked = 0,
    NotConnected = 1,
    OwnerNotFound = 2,
}

public sealed record StoredApiKeyConnectionState(
    StoredApiKeyConnectionStatus Status,
    int? TornPlayerId,
    DateTimeOffset? StoredAtUtc,
    string? ConsentDocumentVersion,
    string? ConsentPurpose,
    DateTimeOffset? ConsentAcceptedAtUtc);

/// <summary>
/// Persists member Torn API keys only after the owning anonymous identity and a current,
/// unrevoked consent record have been proven inside the same serializable transaction.
/// </summary>
public sealed class StoredApiKeyStore
{
    private readonly HappyGymStatsDbContext _db;
    private readonly Func<WarKeyVault> _vaultFactory;
    private readonly TimeProvider _timeProvider;

    public StoredApiKeyStore(
        HappyGymStatsDbContext db,
        WarKeyVault vault,
        TimeProvider? timeProvider = null)
        : this(db, () => vault, timeProvider)
    {
    }

    public StoredApiKeyStore(
        HappyGymStatsDbContext db,
        Func<WarKeyVault> vaultFactory,
        TimeProvider? timeProvider = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _vaultFactory = vaultFactory ?? throw new ArgumentNullException(nameof(vaultFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StoredApiKeyWriteStatus> StoreAsync(
        Guid anonymousId,
        int tornPlayerId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateWriteInputs(anonymousId, tornPlayerId, apiKey);

        var storedAtUtc = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);

        if (!await OwnerExistsAsync(anonymousId, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyWriteStatus.OwnerNotFound;
        }

        var consent = await GetLatestCurrentConsentAsync(anonymousId, storedAtUtc, cancellationToken)
            .ConfigureAwait(false);
        if (consent is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyWriteStatus.ConsentRequired;
        }

        var ciphertext = _vaultFactory().Protect(apiKey.AsSpan(), tornPlayerId, ConsentPurposes.WarMemberApiKey);
        await UpsertStoredKeyAsync(
            anonymousId,
            tornPlayerId,
            ciphertext,
            consent.Id,
            storedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return StoredApiKeyWriteStatus.Stored;
    }

    /// <summary>
    /// Stores or replaces a member key after the caller has explicitly accepted the current
    /// key-storage disclosure. This is the member-facing orchestration path; <see cref="StoreAsync"/>
    /// remains the strict primitive for callers that must prove consent already exists.
    /// </summary>
    public async Task<StoredApiKeyWriteStatus> StoreWithConsentAsync(
        Guid anonymousId,
        int tornPlayerId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateWriteInputs(anonymousId, tornPlayerId, apiKey);

        var storedAtUtc = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);

        if (!await OwnerExistsAsync(anonymousId, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyWriteStatus.OwnerNotFound;
        }

        var consent = await GetLatestCurrentConsentAsync(anonymousId, storedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        // Resolve and use the vault before writing a new consent row. A missing/malformed master
        // key must leave no misleading "accepted" record for a connection that could not be stored.
        var ciphertext = _vaultFactory().Protect(apiKey.AsSpan(), tornPlayerId, ConsentPurposes.WarMemberApiKey);

        if (consent is null)
        {
            consent = new ConsentRecordEntity
            {
                AnonymousId = anonymousId,
                DocumentVersion = TermsDocument.Version,
                Purpose = ConsentPurposes.WarMemberApiKey,
                AcceptedAtUtc = storedAtUtc,
            };
            _db.ConsentRecords.Add(consent);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertStoredKeyAsync(
            anonymousId,
            tornPlayerId,
            ciphertext,
            consent.Id,
            storedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return StoredApiKeyWriteStatus.Stored;
    }

    public async Task<StoredApiKeyConnectionState> GetConnectionStateAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
    {
        if (anonymousId == Guid.Empty)
        {
            throw new ArgumentException("Anonymous id must be non-empty.", nameof(anonymousId));
        }

        if (!await OwnerExistsAsync(anonymousId, cancellationToken).ConfigureAwait(false))
        {
            return new StoredApiKeyConnectionState(
                StoredApiKeyConnectionStatus.OwnerNotFound,
                null,
                null,
                null,
                null,
                null);
        }

        var now = _timeProvider.GetUtcNow();
        var storedKey = await _db.StoredApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AnonymousId == anonymousId, cancellationToken)
            .ConfigureAwait(false);

        if (storedKey is not null)
        {
            var referencedConsent = await _db.ConsentRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == storedKey.ConsentRecordId
                        && x.AnonymousId == anonymousId
                        && x.Purpose == ConsentPurposes.WarMemberApiKey
                        && x.DocumentVersion == TermsDocument.Version
                        && x.RevokedAtUtc == null
                        && x.AcceptedAtUtc <= now,
                    cancellationToken)
                .ConfigureAwait(false);

            if (referencedConsent is not null)
            {
                return new StoredApiKeyConnectionState(
                    StoredApiKeyConnectionStatus.Connected,
                    storedKey.TornPlayerId,
                    storedKey.StoredAtUtc,
                    referencedConsent.DocumentVersion,
                    referencedConsent.Purpose,
                    referencedConsent.AcceptedAtUtc);
            }
        }

        var activeConsent = await GetLatestCurrentConsentAsync(anonymousId, now, cancellationToken)
            .ConfigureAwait(false);

        return new StoredApiKeyConnectionState(
            StoredApiKeyConnectionStatus.NotConnected,
            null,
            null,
            activeConsent?.DocumentVersion,
            activeConsent?.Purpose,
            activeConsent?.AcceptedAtUtc);
    }

    public async Task<StoredApiKeyRevokeStatus> RevokeAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
    {
        if (anonymousId == Guid.Empty)
        {
            throw new ArgumentException("Anonymous id must be non-empty.", nameof(anonymousId));
        }

        var revokedAtUtc = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);

        if (!await OwnerExistsAsync(anonymousId, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyRevokeStatus.OwnerNotFound;
        }

        var storedKey = await _db.StoredApiKeys
            .SingleOrDefaultAsync(x => x.AnonymousId == anonymousId, cancellationToken)
            .ConfigureAwait(false);
        var activeConsents = await _db.ConsentRecords
            .Where(x => x.AnonymousId == anonymousId
                && x.Purpose == ConsentPurposes.WarMemberApiKey
                && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (storedKey is null && activeConsents.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyRevokeStatus.NotConnected;
        }

        if (storedKey is not null)
        {
            _db.StoredApiKeys.Remove(storedKey);
        }

        foreach (var consent in activeConsents)
        {
            consent.RevokedAtUtc = revokedAtUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return StoredApiKeyRevokeStatus.Revoked;
    }

    private async Task<bool> OwnerExistsAsync(Guid anonymousId, CancellationToken cancellationToken)
        => await _db.IdentityMap
            .AsNoTracking()
            .AnyAsync(x => x.AnonymousId == anonymousId, cancellationToken)
            .ConfigureAwait(false);

    private async Task<ConsentRecordEntity?> GetLatestCurrentConsentAsync(
        Guid anonymousId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
        => await _db.ConsentRecords
            .AsNoTracking()
            .Where(x => x.AnonymousId == anonymousId
                && x.Purpose == ConsentPurposes.WarMemberApiKey
                && x.DocumentVersion == TermsDocument.Version
                && x.RevokedAtUtc == null
                && x.AcceptedAtUtc <= atUtc)
            .OrderByDescending(x => x.AcceptedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task UpsertStoredKeyAsync(
        Guid anonymousId,
        int tornPlayerId,
        byte[] ciphertext,
        long consentRecordId,
        DateTimeOffset storedAtUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _db.StoredApiKeys
            .SingleOrDefaultAsync(x => x.AnonymousId == anonymousId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.StoredApiKeys.Add(new StoredApiKeyEntity
            {
                AnonymousId = anonymousId,
                TornPlayerId = tornPlayerId,
                Ciphertext = ciphertext,
                ConsentRecordId = consentRecordId,
                StoredAtUtc = storedAtUtc,
            });
        }
        else
        {
            existing.TornPlayerId = tornPlayerId;
            existing.Ciphertext = ciphertext;
            existing.ConsentRecordId = consentRecordId;
            existing.StoredAtUtc = storedAtUtc;
        }
    }

    private static void ValidateWriteInputs(Guid anonymousId, int tornPlayerId, string apiKey)
    {
        if (anonymousId == Guid.Empty)
        {
            throw new ArgumentException("Anonymous id must be non-empty.", nameof(anonymousId));
        }

        if (tornPlayerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tornPlayerId), "Torn player id must be positive.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must be non-empty.", nameof(apiKey));
        }
    }
}
