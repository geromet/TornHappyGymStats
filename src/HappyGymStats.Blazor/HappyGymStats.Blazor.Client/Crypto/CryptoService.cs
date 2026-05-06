using System.Security.Cryptography;
using System.Text;
using HappyGymStats.Encryption;
using Microsoft.JSInterop;

namespace HappyGymStats.Blazor.Client.Crypto;

public sealed class CryptoService(IJSRuntime js)
{
    private const string StorageKey = "happygymstats.wrapped_key";

    public async Task<bool> HasStoredKeyAsync()
        => await js.InvokeAsync<string?>("localStorage.getItem", StorageKey) is not null;

    /// <summary>
    /// Generates a new P-256 ECDH keypair. Returns the SPKI-encoded public key (for sending to
    /// the server) and the PBKDF2+AES-GCM-wrapped private key blob (for localStorage).
    /// </summary>
    public static (byte[] PublicKeySpki, byte[] WrappedPrivateKey) GenerateKeyPair(string password)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdh.ExportSubjectPublicKeyInfo();
        var privateKey = ecdh.ExportPkcs8PrivateKey();
        var wrapped = KeyWrapping.WrapKey(privateKey, password.AsSpan());
        return (publicKey, wrapped);
    }

    public async Task StoreWrappedKeyAsync(byte[] wrappedKey)
        => await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Convert.ToBase64String(wrappedKey));

    /// <summary>
    /// Loads and unwraps the stored private key. Returns null if no key is stored or the
    /// password is wrong.
    /// </summary>
    public async Task<ECDiffieHellman?> LoadKeyAsync(string password)
    {
        var base64 = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (base64 is null) return null;
        try
        {
            var wrapped = Convert.FromBase64String(base64);
            return KeyWrapping.UnwrapKey(wrapped, password.AsSpan());
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decrypts an ECIES ciphertext blob returned by the API, returning the plaintext as a
    /// UTF-8 string (e.g. "123456" for a Torn player ID).
    /// </summary>
    public static string DecryptToString(ECDiffieHellman privateKey, byte[] encryptedBlob)
        => Encoding.UTF8.GetString(Ecies.Decrypt(privateKey, encryptedBlob));
}
