namespace HappyGymStats.Api;

// PublicKey: base64-encoded P-256 SPKI bytes from the client's in-browser keypair.
// When present, the server stores it in IdentityMap and encrypts the Torn player ID with it.
public sealed record ImportRequest(string? ApiKey, bool? Fresh, string? PublicKey);
