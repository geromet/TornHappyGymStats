using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HappyGymStats.Encryption;

/// <summary>
/// Wraps/unwraps an ECDiffieHellman private key using PBKDF2-SHA256 + AES-256-GCM.
/// Wire format: [4 iterations BE] [32 salt] [12 nonce] [M ciphertext] [16 tag]
/// </summary>
public static class KeyWrapping
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int AesKeySize = 32;
    private const int DefaultIterations = 100_000;

    public static byte[] WrapKey(ReadOnlySpan<byte> privateKeyPkcs8, ReadOnlySpan<char> password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, AesKeySize);

        var ciphertext = new byte[privateKeyPkcs8.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, tagSizeInBytes: TagSize))
            aes.Encrypt(nonce, privateKeyPkcs8, ciphertext, tag);

        var output = new byte[4 + SaltSize + NonceSize + ciphertext.Length + TagSize];
        var span = output.AsSpan();
        BinaryPrimitives.WriteInt32BigEndian(span, iterations);
        salt.CopyTo(span[4..]);
        nonce.CopyTo(span[(4 + SaltSize)..]);
        var off = 4 + SaltSize + NonceSize;
        ciphertext.CopyTo(span[off..]);
        tag.CopyTo(span[(off + ciphertext.Length)..]);
        return output;
    }

    public static ECDiffieHellman UnwrapKey(ReadOnlySpan<byte> wrapped, ReadOnlySpan<char> password)
    {
        if (wrapped.Length < 4 + SaltSize + NonceSize + TagSize)
            throw new CryptographicException("Wrapped key blob too short.");

        var iterations = BinaryPrimitives.ReadInt32BigEndian(wrapped);
        var salt = wrapped.Slice(4, SaltSize);
        var nonce = wrapped.Slice(4 + SaltSize, NonceSize);
        var off = 4 + SaltSize + NonceSize;
        var ciphertextLen = wrapped.Length - off - TagSize;
        if (ciphertextLen < 0)
            throw new CryptographicException("Wrapped key blob too short.");
        var ciphertext = wrapped.Slice(off, ciphertextLen);
        var tag = wrapped.Slice(off + ciphertextLen, TagSize);

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, AesKeySize);
        var plaintext = new byte[ciphertextLen];

        using (var aes = new AesGcm(key, tagSizeInBytes: TagSize))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var ecKey = ECDiffieHellman.Create();
        ecKey.ImportPkcs8PrivateKey(plaintext, out _);
        return ecKey;
    }
}
