namespace HappyGymStats.Identity.Provisional;

public interface IProvisionalTokenService
{
    string Issue(Guid anonymousId);

    /// <summary>Returns the AnonymousId embedded in the token, or null if the token is invalid or expired.</summary>
    Guid? Validate(string token);
}
