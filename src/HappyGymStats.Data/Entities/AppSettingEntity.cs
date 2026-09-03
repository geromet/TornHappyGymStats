namespace HappyGymStats.Data.Entities;

/// <summary>
/// A single runtime-adjustable setting, stored as a key/value pair.
///
/// Deliberately generic rather than a column per feature: these are operational
/// switches an administrator flips at runtime, and adding the next one should
/// not require a migration. The first is the gym point-cloud toggle, so the page
/// can be shut off during a war without a redeploy.
/// </summary>
public sealed class AppSettingEntity
{
    /// <summary>Stable identifier, e.g. "ui.gym-point-cloud.enabled".</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Who last changed it. The anonymous id or username of the acting admin —
    /// never a Torn player id, per the project's pseudonymisation rule.
    /// </summary>
    public string? UpdatedBy { get; set; }
}
