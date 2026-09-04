namespace HappyGymStats.Data.Entities;

/// <summary>
/// Server-held encrypted Torn API key for one member identity.
///
/// The credential itself is never represented as a string property. <see cref="TornPlayerId"/>
/// is retained because <c>WarKeyVault</c> binds it into AES-GCM associated data and therefore
/// requires the same value to authenticate the ciphertext when the key is used later.
/// </summary>
public sealed class StoredApiKeyEntity
{
    public Guid AnonymousId { get; set; }
    public int TornPlayerId { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public long ConsentRecordId { get; set; }
    public DateTimeOffset StoredAtUtc { get; set; }
}
