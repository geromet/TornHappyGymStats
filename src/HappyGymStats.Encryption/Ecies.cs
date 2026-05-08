using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HappyGymStats.Encryption;

/// <summary>
/// Hybrid encryption using ephemeral P-256 ECDH-ES + HKDF-SHA256 + AES-256-GCM.
/// Wire format: [1 version] [2 ephem-key-len BE] [N ephem SPKI] [12 nonce] [M ciphertext] [16 tag]
/// </summary>
public static class Ecies
{
    private const byte Version = 0x01;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] HkdfInfo = "happygymstats-ecies-v1"u8.ToArray();

    public static byte[] Encrypt(ReadOnlySpan<byte> recipientPublicKeySpki, ReadOnlySpan<byte> plaintext)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var recipientEcdh = ECDiffieHellman.Create();
        recipientEcdh.ImportSubjectPublicKeyInfo(recipientPublicKeySpki, out _);

        var sharedSecret = ephemeral.DeriveRawSecretAgreement(recipientEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, info: HkdfInfo);

        var ephemeralSpki = ephemeral.ExportSubjectPublicKeyInfo();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(aesKey, tagSizeInBytes: TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[1 + 2 + ephemeralSpki.Length + NonceSize + ciphertext.Length + TagSize];
        var span = output.AsSpan();
        span[0] = Version;
        BinaryPrimitives.WriteUInt16BigEndian(span[1..], (ushort)ephemeralSpki.Length);
        ephemeralSpki.CopyTo(span[3..]);
        var off = 3 + ephemeralSpki.Length;
        nonce.CopyTo(span[off..]);
        off += NonceSize;
        ciphertext.CopyTo(span[off..]);
        off += ciphertext.Length;
        tag.CopyTo(span[off..]);
        return output;
    }

    public static byte[] Decrypt(ECDiffieHellman recipientKey, ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 3)
            throw new CryptographicException("Blob too short.");
        if (blob[0] != Version)
            throw new CryptographicException($"Unsupported ECIES version 0x{blob[0]:X2}.");

        int ephemeralKeyLen = BinaryPrimitives.ReadUInt16BigEndian(blob[1..]);
        if (blob.Length < 3 + ephemeralKeyLen + NonceSize + TagSize)
            throw new CryptographicException("Blob too short.");

        var ephemeralSpki = blob.Slice(3, ephemeralKeyLen);
        var off = 3 + ephemeralKeyLen;
        var nonce = blob.Slice(off, NonceSize);
        off += NonceSize;
        var ciphertextLen = blob.Length - off - TagSize;
        if (ciphertextLen < 0)
            throw new CryptographicException("Blob too short.");
        var ciphertext = blob.Slice(off, ciphertextLen);
        var tag = blob.Slice(off + ciphertextLen, TagSize);

        using var ephemeralKey = ECDiffieHellman.Create();
        ephemeralKey.ImportSubjectPublicKeyInfo(ephemeralSpki, out _);

        var sharedSecret = recipientKey.DeriveRawSecretAgreement(ephemeralKey.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, info: HkdfInfo);

        var plaintext = new byte[ciphertextLen];
        using (var aes = new AesGcm(aesKey, tagSizeInBytes: TagSize))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
