using System.Security.Cryptography;

namespace HappyGymStats.Encryption;

/// <summary>
/// Validates and imports the P-256 SubjectPublicKeyInfo contract used by ECIES.
/// </summary>
public static class P256PublicKey
{
    private static readonly string? P256Oid = ECCurve.NamedCurves.nistP256.Oid.Value;

    public static bool IsValidSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            using var _ = ImportSubjectPublicKeyInfo(subjectPublicKeyInfo);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static ECDiffieHellman ImportSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
            throw new CryptographicException("Public key is empty.");

        var ecdh = ECDiffieHellman.Create();
        try
        {
            ecdh.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
                throw new CryptographicException("Public key contains trailing data.");

            var parameters = ecdh.ExportParameters(includePrivateParameters: false);
            if (string.IsNullOrWhiteSpace(P256Oid)
                || !string.Equals(parameters.Curve.Oid.Value, P256Oid, StringComparison.Ordinal))
            {
                throw new CryptographicException("Public key must use the P-256 curve.");
            }

            return ecdh;
        }
        catch
        {
            ecdh.Dispose();
            throw;
        }
    }
}
