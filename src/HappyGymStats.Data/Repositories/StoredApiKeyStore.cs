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

/// <summary>
/// Persists member Torn API keys only after the owning anonymous identity and a current,
/// unrevoked consent record have been proven inside the same serializable transaction.
/// </summary>
public sealed class StoredApiKeyStore
{
    private readonly HappyGymStatsDbContext _db;
    private readonly WarKeyVault _vault;
    private readonly TimeProvider _timeProvider;

    public StoredApiKeyStore(
        HappyGymStatsDbContext db,
        WarKeyVault vault,
        TimeProvider? timeProvider = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StoredApiKeyWriteStatus> StoreAsync(
        Guid anonymousId,
        int tornPlayerId,
        string apiKey,
        CancellationToken cancellationToken = default)
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

        var storedAtUtc = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);

        var ownerExists = await _db.IdentityMap
            .AsNoTracking()
            .AnyAsync(x => x.AnonymousId == anonymousId, cancellationToken)
            .ConfigureAwait(false);
        if (!ownerExists)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyWriteStatus.OwnerNotFound;
        }

        var consent = await _db.ConsentRecords
            .AsNoTracking()
            .Where(x => x.AnonymousId == anonymousId
                && x.Purpose == ConsentPurposes.WarMemberApiKey
                && x.DocumentVersion == TermsDocument.Version
                && x.RevokedAtUtc == null
                && x.AcceptedAtUtc <= storedAtUtc)
            .OrderByDescending(x => x.AcceptedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (consent is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StoredApiKeyWriteStatus.ConsentRequired;
        }

        var ciphertext = _vault.Protect(apiKey.AsSpan(), tornPlayerId, ConsentPurposes.WarMemberApiKey);
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
                ConsentRecordId = consent.Id,
                StoredAtUtc = storedAtUtc,
            });
        }
        else
        {
            existing.TornPlayerId = tornPlayerId;
            existing.Ciphertext = ciphertext;
            existing.ConsentRecordId = consent.Id;
            existing.StoredAtUtc = storedAtUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return StoredApiKeyWriteStatus.Stored;
    }
}
