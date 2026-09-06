namespace HappyGymStats.Identity.Provisional;

public interface IProvisionalTokenService
{
    IssuedProvisionalToken Issue(Guid anonymousId);

    /// <summary>Returns the AnonymousId embedded in the token, or null if the token is invalid or expired.</summary>
    Guid? Validate(string token);
}

public sealed record IssuedProvisionalToken(string Value, DateTimeOffset ExpiresAtUtc);
