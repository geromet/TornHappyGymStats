using HappyGymStats.Api.Services;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Deterministic account state for local rendered proof. Program only registers
/// this fixture while the explicitly gated development-auth host is active.
/// </summary>
internal sealed class DevelopmentAccountConnectionFixture : IAccountConnectionService
{
    public const string ConfigurationKey = "HAPPYGYMSTATS_DEV_ACCOUNT_FIXTURE";
    private const string ConnectedMode = "connected";
    private const string ReplacementErrorMode = "replacement-error";

    private static readonly AccountConnectionSnapshot Connected = new(
        Connected: true,
        TornPlayerId: 24680,
        StoredAtUtc: new DateTimeOffset(2030, 6, 15, 12, 30, 45, TimeSpan.Zero),
        Consent: new AccountConsentSnapshot(
            "terms-v1",
            "Personal Torn connection for Happy Gym Stats",
            new DateTimeOffset(2030, 6, 15, 12, 29, 0, TimeSpan.Zero)));

    private static readonly AccountConnectionSnapshot NotConnected = new(
        Connected: false,
        TornPlayerId: null,
        StoredAtUtc: null,
        Consent: null);

    private readonly string _mode;

    private DevelopmentAccountConnectionFixture(string mode)
    {
        _mode = mode;
    }

    public static DevelopmentAccountConnectionFixture? Create(
        IConfiguration configuration,
        bool developmentAuthEnabled)
    {
        var mode = configuration[ConfigurationKey]?.Trim();
        if (string.IsNullOrEmpty(mode))
            return null;

        if (!developmentAuthEnabled)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} requires the explicitly enabled development authentication host.");
        }

        if (mode is not (ConnectedMode or ReplacementErrorMode))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be '{ConnectedMode}' or '{ReplacementErrorMode}'.");
        }

        return new DevelopmentAccountConnectionFixture(mode);
    }

    public Task<AccountConnectionOperationResult> GetStatusAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Success(Connected));

    public Task<AccountConnectionOperationResult> ConnectAsync(
        Guid anonymousId,
        string? tornApiKey,
        bool consentAccepted,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            _mode == ReplacementErrorMode
                ? new AccountConnectionOperationResult(AccountConnectionOperationStatus.InvalidTornApiKey)
                : Success(Connected));

    public Task<AccountConnectionOperationResult> RevokeAsync(
        Guid anonymousId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Success(NotConnected));

    private static AccountConnectionOperationResult Success(AccountConnectionSnapshot snapshot)
        => new(AccountConnectionOperationStatus.Success, snapshot);
}
