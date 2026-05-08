namespace HappyGymStats.Identity.Provisional;

public sealed class ProvisionalTokenOptions
{
    public const string Section = "ProvisionalToken";

    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 24;
}
