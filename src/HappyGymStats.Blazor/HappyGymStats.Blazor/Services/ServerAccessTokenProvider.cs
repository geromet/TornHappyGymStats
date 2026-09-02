using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace HappyGymStats.Blazor.Services;

public interface IServerAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync();
}

public sealed class ServerAccessTokenProvider(
    IHttpContextAccessor httpContextAccessor,
    PersistentComponentState persistentComponentState,
    ILogger<ServerAccessTokenProvider> logger) : IServerAccessTokenProvider, IDisposable
{
    private const string AccessTokenStateKey = "happygymstats-access-token";

    private PersistingComponentStateSubscription? persistingSubscription;
    private bool initialized;

    public string? AccessToken { get; private set; }

    public async Task<string?> GetAccessTokenAsync()
    {
        if (!initialized)
        {
            await InitializeAsync();
        }

        return AccessToken;
    }

    private async Task InitializeAsync()
    {
        initialized = true;

        if (persistentComponentState.TryTakeFromJson<string>(AccessTokenStateKey, out var persistedToken) &&
            !string.IsNullOrWhiteSpace(persistedToken))
        {
            AccessToken = persistedToken;
            return;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            logger.LogDebug("No HttpContext was available while initializing the server access token provider.");
            return;
        }

        var accessToken = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("The signed-in Blazor user does not have a saved access_token. Downstream API and hub calls will be unauthorized until OIDC token saving/audience configuration is fixed.");
            return;
        }

        AccessToken = accessToken;
        persistingSubscription = persistentComponentState.RegisterOnPersisting(PersistAccessTokenAsync);
    }

    private Task PersistAccessTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            persistentComponentState.PersistAsJson(AccessTokenStateKey, AccessToken);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        persistingSubscription?.Dispose();
    }
}
