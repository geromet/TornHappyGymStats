using Microsoft.JSInterop;

namespace HappyGymStats.Blazor.Client.Crypto;

public sealed class CryptoService(IJSRuntime js)
{
    private const string StorageKey = "happygymstats.wrapped_key";
    private const string PublicKeyStorageKey = "happygymstats.public_key";
    private const string ModulePath = "./crypto.js";

    public async Task<bool> HasStoredKeyAsync()
        => await js.InvokeAsync<string?>("localStorage.getItem", StorageKey) is not null;

    public async Task<string> GenerateAndStoreKeyAsync(string password)
    {
        await using var module = await ImportModuleAsync();
        var generated = await module.InvokeAsync<GeneratedKeyPair>("generateWrappedKeyPair", password);

        // Validate that browser interop returned well-formed base64 before persisting it.
        _ = Convert.FromBase64String(generated.PublicKeySpki);
        _ = Convert.FromBase64String(generated.WrappedPrivateKey);

        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, generated.WrappedPrivateKey);
        await js.InvokeVoidAsync("localStorage.setItem", PublicKeyStorageKey, generated.PublicKeySpki);
        return generated.PublicKeySpki;
    }

    public async Task<string?> VerifyStoredKeyAsync(string password)
    {
        var wrappedKey = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (wrappedKey is null)
            return null;

        await using var module = await ImportModuleAsync();
        var publicKeySpki = await module.InvokeAsync<string?>("unwrapPublicKeySpki", wrappedKey, password);
        if (publicKeySpki is null)
            return null;

        _ = Convert.FromBase64String(publicKeySpki);
        await js.InvokeVoidAsync("localStorage.setItem", PublicKeyStorageKey, publicKeySpki);
        return publicKeySpki;
    }

    public async Task<string?> GetPublicKeyBase64Async()
        => await js.InvokeAsync<string?>("localStorage.getItem", PublicKeyStorageKey);

    public async Task DeleteKeyAsync()
    {
        // Delete the recoverable public-key cache first. If either storage operation fails,
        // the wrapped private key is never removed before the cache cleanup succeeds. This keeps
        // the page's "Key stored" state truthful and leaves a failed deletion safely retryable.
        await js.InvokeVoidAsync("localStorage.removeItem", PublicKeyStorageKey);
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    private ValueTask<IJSObjectReference> ImportModuleAsync()
        => js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    private sealed class GeneratedKeyPair
    {
        public required string PublicKeySpki { get; init; }
        public required string WrappedPrivateKey { get; init; }
    }
}
