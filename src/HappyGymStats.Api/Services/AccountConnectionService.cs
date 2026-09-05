using HappyGymStats.Data.Repositories;
using HappyGymStats.Core.War;

namespace HappyGymStats.Api.Services;

public enum AccountConnectionOperationStatus
{
    Success = 0,
    OwnerNotFound = 1,
    ConsentRequired = 2,
    InvalidTornApiKey = 3,
    TornUnavailable = 4,
    KeyVaultUnavailable = 5,
    NotConnected = 6,
}

public sealed record AccountConsentSnapshot(
    string DocumentVersion,
    string Purpose,
    DateTimeOffset AcceptedAtUtc);

public sealed record AccountConnectionSnapshot(
    bool Connected,
    int? TornPlayerId,
    DateTimeOffset? StoredAtUtc,
    AccountConsentSnapshot? Consent);

public sealed record AccountConnectionOperationResult(
    AccountConnectionOperationStatus Status,
    AccountConnectionSnapshot? Connection = null);

public interface IAccountConnectionService
{
    Task<AccountConnectionOperationResult> GetStatusAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default);

    Task<AccountConnectionOperationResult> ConnectAsync(
        Guid anonymousId,
        string? tornApiKey,
        bool consentAccepted,
        CancellationToken cancellationToken = default);

    Task<AccountConnectionOperationResult> RevokeAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single application seam for the authenticated member Torn-connection lifecycle.
/// Ownership is supplied by the controller from trusted claims; no caller-controlled owner id
/// is accepted here or by the HTTP request contract.
/// </summary>
public sealed class AccountConnectionService : IAccountConnectionService
{
    private readonly StoredApiKeyStore _store;
    private readonly ITornConnectionValidator _validator;

    public AccountConnectionService(
        StoredApiKeyStore store,
        ITornConnectionValidator validator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<AccountConnectionOperationResult> GetStatusAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
    {
        var state = await _store
            .GetConnectionStateAsync(anonymousId, cancellationToken)
            .ConfigureAwait(false);

        return state.Status == StoredApiKeyConnectionStatus.OwnerNotFound
            ? new AccountConnectionOperationResult(AccountConnectionOperationStatus.OwnerNotFound)
            : new AccountConnectionOperationResult(
                AccountConnectionOperationStatus.Success,
                ToSnapshot(state));
    }

    public async Task<AccountConnectionOperationResult> ConnectAsync(
        Guid anonymousId,
        string? tornApiKey,
        bool consentAccepted,
        CancellationToken cancellationToken = default)
    {
        var current = await _store
            .GetConnectionStateAsync(anonymousId, cancellationToken)
            .ConfigureAwait(false);
        if (current.Status == StoredApiKeyConnectionStatus.OwnerNotFound)
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.OwnerNotFound);
        }

        if (!consentAccepted)
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.ConsentRequired);
        }

        var candidate = tornApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.InvalidTornApiKey);
        }

        int tornPlayerId;
        try
        {
            tornPlayerId = await _validator
                .GetPlayerIdAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TornConnectionValidationException ex)
        {
            return new AccountConnectionOperationResult(
                ex.IsTransient
                    ? AccountConnectionOperationStatus.TornUnavailable
                    : AccountConnectionOperationStatus.InvalidTornApiKey);
        }

        StoredApiKeyWriteStatus writeStatus;
        try
        {
            writeStatus = await _store
                .StoreWithConsentAsync(anonymousId, tornPlayerId, candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WarKeyVaultConfigurationException)
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.KeyVaultUnavailable);
        }

        if (writeStatus == StoredApiKeyWriteStatus.OwnerNotFound)
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.OwnerNotFound);
        }

        if (writeStatus == StoredApiKeyWriteStatus.ConsentRequired)
        {
            return new AccountConnectionOperationResult(AccountConnectionOperationStatus.ConsentRequired);
        }

        return await GetStatusAsync(anonymousId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountConnectionOperationResult> RevokeAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
    {
        var status = await _store
            .RevokeAsync(anonymousId, cancellationToken)
            .ConfigureAwait(false);

        return status switch
        {
            StoredApiKeyRevokeStatus.OwnerNotFound
                => new AccountConnectionOperationResult(AccountConnectionOperationStatus.OwnerNotFound),
            StoredApiKeyRevokeStatus.NotConnected
                => new AccountConnectionOperationResult(AccountConnectionOperationStatus.NotConnected),
            _ => await GetStatusAsync(anonymousId, cancellationToken).ConfigureAwait(false),
        };
    }

    private static AccountConnectionSnapshot ToSnapshot(StoredApiKeyConnectionState state)
    {
        var consent = state.ConsentDocumentVersion is not null
            && state.ConsentPurpose is not null
            && state.ConsentAcceptedAtUtc is not null
            ? new AccountConsentSnapshot(
                state.ConsentDocumentVersion,
                state.ConsentPurpose,
                state.ConsentAcceptedAtUtc.Value)
            : null;

        return new AccountConnectionSnapshot(
            state.Status == StoredApiKeyConnectionStatus.Connected,
            state.TornPlayerId,
            state.StoredAtUtc,
            consent);
    }
}
