namespace HappyGymStats.Data.Entities;

/// <summary>
/// Records an explicit member acceptance of a published data-use disclosure.
/// Consent is keyed to the privacy-preserving HappyGymStats anonymous identity;
/// raw Torn player IDs and API keys never belong in this row.
/// </summary>
public sealed class ConsentRecordEntity
{
    public long Id { get; set; }
    public Guid AnonymousId { get; set; }
    public string DocumentVersion { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public static class ConsentPurposes
{
    /// <summary>
    /// Consent to store and use an encrypted member Torn API key for exact war telemetry.
    /// </summary>
    public const string WarMemberApiKey = "war-member-api-key";
}
