using System.Security.Cryptography;
using System.Text;

namespace HappyGymStats.Core.War;

/// <summary>
/// Envelope encryption for stored Torn API keys (M009 S02, → workspace/V2/handoff/07).
///
/// Deliberately NOT <c>Encryption.Ecies</c>. That scheme encrypts to a client-held public
/// key precisely so the server cannot decrypt, which is the correct property for member
/// gym data and the wrong one for a key the server must use unattended. Reusing it here
/// because the two look similar would produce a vault that cannot open.
///
/// It also does not reuse <c>Encryption.KeyWrapping</c>'s wire format verbatim. That frame
/// leads with <c>[4 iterations BE][32 salt]</c> because its key is derived from a password
/// via PBKDF2. <c>WAR_KEY_MASTER</c> is already key material, so an iteration count and a
/// salt would be two fields that describe nothing. The AES-GCM conventions are shared
/// (12-byte nonce, 16-byte tag, nonce ∥ ciphertext ∥ tag); the header is a version byte so
/// the format can change when the master key rotates.
///
/// Wire format: [1 version] [12 nonce] [N ciphertext] [16 tag]
/// </summary>
public sealed class WarKeyVault
{
    /// <summary>Environment variable holding the base64 master key. Never in git, never in appsettings.</summary>
    public const string MasterKeyEnvironmentVariable = "WAR_KEY_MASTER";

    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MasterKeySize = 32;
    private const int HeaderSize = 1 + NonceSize;

    private readonly byte[] _masterKey;

    /// <param name="masterKey">32 bytes of key material. Callers should prefer <see cref="FromEnvironment"/>.</param>
    public WarKeyVault(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.Length != MasterKeySize)
        {
            // Length only. The value must not reach an exception message, and neither
            // must anything derived from it.
            throw new WarKeyVaultConfigurationException(
                $"{MasterKeyEnvironmentVariable} must decode to exactly {MasterKeySize} bytes; got {masterKey.Length}.");
        }

        _masterKey = masterKey.ToArray();
    }

    /// <summary>
    /// Builds a vault from <see cref="MasterKeyEnvironmentVariable"/>.
    ///
    /// Throws rather than returning a half-working vault, but callers are expected to
    /// register this LAZILY: an unconfigured master key must disable key-linked features,
    /// not prevent the API from starting. A whole site taken down by one missing secret is
    /// the worse failure — see the Blazor host's Keycloak:RequireClientSecret handling.
    /// </summary>
    public static WarKeyVault FromEnvironment(IDictionary<string, string?>? environment = null)
    {
        var raw = environment is not null
            ? environment.TryGetValue(MasterKeyEnvironmentVariable, out var fromDictionary) ? fromDictionary : null
            : Environment.GetEnvironmentVariable(MasterKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new WarKeyVaultConfigurationException(
                $"{MasterKeyEnvironmentVariable} is not set. Key-linked features are unavailable until it is. " +
                $"Generate one with: openssl rand -base64 {MasterKeySize}");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException)
        {
            // No inner exception: FormatException's message quotes the offending input.
            throw new WarKeyVaultConfigurationException(
                $"{MasterKeyEnvironmentVariable} is not valid base64.");
        }

        try
        {
            return new WarKeyVault(decoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    /// <summary>
    /// Encrypts an API key for storage.
    ///
    /// <paramref name="playerId"/> and <paramref name="purpose"/> are bound in as AES-GCM
    /// associated data, not stored inside the ciphertext. A blob lifted from one member's
    /// row into another's therefore fails authentication instead of quietly decrypting
    /// into the wrong identity — which would defeat both per-member revocation and the
    /// promise that a revoked member's readings are deleted.
    /// </summary>
    public byte[] Protect(ReadOnlySpan<char> apiKey, int playerId, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var plaintextLength = Encoding.UTF8.GetByteCount(apiKey);
        var plaintext = new byte[plaintextLength];

        try
        {
            Encoding.UTF8.GetBytes(apiKey, plaintext);

            var output = new byte[HeaderSize + plaintextLength + TagSize];
            output[0] = FormatVersion;

            var nonce = output.AsSpan(1, NonceSize);
            RandomNumberGenerator.Fill(nonce);

            using var aes = new AesGcm(_masterKey, TagSize);
            aes.Encrypt(
                nonce,
                plaintext,
                output.AsSpan(HeaderSize, plaintextLength),
                output.AsSpan(HeaderSize + plaintextLength, TagSize),
                AssociatedData(playerId, purpose));

            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Decrypts a stored key and hands it to <paramref name="use"/>.
    ///
    /// Callback-shaped on purpose: there is no <c>Unprotect</c> returning a string, because
    /// a returned key is one a caller can put in a field, a static, or a closure that
    /// outlives the call — the exact thing handoff 07 forbids. The plaintext buffer is
    /// zeroed when <paramref name="use"/> returns, including when it throws.
    /// </summary>
    public T UseKey<T>(ReadOnlySpan<byte> ciphertext, int playerId, string purpose, Func<string, T> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (ciphertext.Length < HeaderSize + TagSize)
            throw new WarKeyVaultCryptographicException("Stored key blob is too short to be valid.");

        if (ciphertext[0] != FormatVersion)
            throw new WarKeyVaultCryptographicException($"Unsupported stored key format version {ciphertext[0]}.");

        var payloadLength = ciphertext.Length - HeaderSize - TagSize;
        var plaintext = new byte[payloadLength];

        try
        {
            using var aes = new AesGcm(_masterKey, TagSize);
            try
            {
                aes.Decrypt(
                    ciphertext.Slice(1, NonceSize),
                    ciphertext.Slice(HeaderSize, payloadLength),
                    ciphertext.Slice(HeaderSize + payloadLength, TagSize),
                    plaintext,
                    AssociatedData(playerId, purpose));
            }
            catch (CryptographicException)
            {
                // The platform message is generic, but it is not ours and could change.
                // Raise our own, and never attach the inner exception, the ciphertext, or
                // any part of the plaintext.
                throw new WarKeyVaultCryptographicException(
                    "Stored key failed authentication. It was written with a different master key, " +
                    "belongs to another member, or has been altered.");
            }

            return use(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Async overload. Same authentication and same callback shape — but note the zeroing
    /// is NOT held across the caller's await: the plaintext buffer is cleared once the
    /// callback returns its task, before that task completes. That costs nothing here
    /// because the callback receives an immutable <see cref="string"/> the buffer no
    /// longer backs, but do not read this as "the plaintext is protected until the await
    /// finishes". Nothing can protect a .NET string from its own lifetime.
    /// </summary>
    public async Task<T> UseKeyAsync<T>(
        ReadOnlyMemory<byte> ciphertext,
        int playerId,
        string purpose,
        Func<string, Task<T>> use)
    {
        ArgumentNullException.ThrowIfNull(use);

        // Decrypt synchronously so the plaintext never sits in a captured local across an
        // await inside this method; the caller's own await happens after the string is
        // handed over and is their responsibility.
        var task = UseKey(ciphertext.Span, playerId, purpose, use);
        return await task.ConfigureAwait(false);
    }

    private static byte[] AssociatedData(int playerId, string purpose)
        => Encoding.UTF8.GetBytes($"v{FormatVersion}|player={playerId}|purpose={purpose}");
}

/// <summary>The vault is not usable because it is misconfigured. Never carries key material.</summary>
public sealed class WarKeyVaultConfigurationException(string message) : Exception(message);

/// <summary>A stored blob could not be opened. Never carries key material or ciphertext.</summary>
public sealed class WarKeyVaultCryptographicException(string message) : Exception(message);
