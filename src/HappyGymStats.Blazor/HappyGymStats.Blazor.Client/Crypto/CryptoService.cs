using Microsoft.JSInterop;

namespace HappyGymStats.Blazor.Client.Crypto;

public sealed class CryptoService(IJSRuntime js) : IAsyncDisposable
{
    private const string StorageKey = "happygymstats.wrapped_key";
    private const string PublicKeyStorageKey = "happygymstats.public_key";
    private IJSObjectReference? _module;

    public async Task<bool> HasStoredKeyAsync()
        => await js.InvokeAsync<string?>("localStorage.getItem", StorageKey) is not null;

    /// <summary>
    /// Generates a browser-native P-256 ECDH keypair and wraps the PKCS#8 private
    /// key with PBKDF2-SHA256 + AES-256-GCM. The returned wire formats are the
    /// same SPKI and KeyWrapping formats consumed by the server-side encryption
    /// code, but the operation runs through Web Crypto because .NET ECDH is not
    /// supported under browser/WASM.
    /// </summary>
    public async Task<(byte[] PublicKeySpki, byte[] WrappedPrivateKey)> GenerateKeyPairAsync(string password)
    {
        var module = await GetModuleAsync();
        var result = await module.InvokeAsync<BrowserKeyPair>("generateAndWrapKey", password);
        return (
            Convert.FromBase64String(result.PublicKeySpkiBase64),
            Convert.FromBase64String(result.WrappedPrivateKeyBase64));
    }

    public async Task StoreWrappedKeyAsync(byte[] wrappedKey)
        => await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Convert.ToBase64String(wrappedKey));

    public async Task StorePublicKeyAsync(byte[] publicKeySpki)
        => await js.InvokeVoidAsync("localStorage.setItem", PublicKeyStorageKey, Convert.ToBase64String(publicKeySpki));

    public async Task<string?> GetPublicKeyBase64Async()
        => await js.InvokeAsync<string?>("localStorage.getItem", PublicKeyStorageKey);

    public async Task DeleteKeyAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        await js.InvokeVoidAsync("localStorage.removeItem", PublicKeyStorageKey);
    }

    /// <summary>
    /// Unwraps the locally stored private key with Web Crypto and returns its
    /// corresponding SPKI public key. A wrong password or corrupt blob returns
    /// null without exposing the wrapped private key outside the browser.
    /// </summary>
    public async Task<byte[]?> LoadPublicKeyAsync(string password)
    {
        var wrappedBase64 = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (wrappedBase64 is null)
            return null;

        try
        {
            var module = await GetModuleAsync();
            var publicKeyBase64 = await module.InvokeAsync<string>("unwrapPublicKey", password, wrappedBase64);
            return Convert.FromBase64String(publicKeyBase64);
        }
        catch (JSException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./crypto.js");

    private sealed record BrowserKeyPair(string PublicKeySpkiBase64, string WrappedPrivateKeyBase64);
}
