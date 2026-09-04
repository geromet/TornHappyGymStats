using Microsoft.AspNetCore.Http.HttpResults;

namespace HappyGymStats.Blazor.Services;

/// <summary>
/// Normalizes untrusted post-authentication destinations using ASP.NET Core's
/// canonical local-URL check. Network-path references such as //evil.example
/// and /\\evil.example are not local even though they start with '/'.
/// </summary>
public static class LocalRedirectPolicy
{
    public static string Normalize(string? returnUrl)
    {
        return RedirectHttpResult.IsLocalUrl(returnUrl)
            ? returnUrl!
            : "/";
    }
}
