using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using HappyGymStats.Core.Import;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class P256PublicKeyAdmissionTests : IDisposable
{
    private readonly SqliteApiEndpointTests.SqliteTestApplicationFactory _factory = new();

    public P256PublicKeyAdmissionTests()
    {
        _factory.ResetDatabase();
    }

    [Fact]
    public void P256_spki_is_accepted_and_ecies_round_trips()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var spki = recipient.ExportSubjectPublicKeyInfo();
        var plaintext = "identity-boundary"u8.ToArray();

        Assert.True(P256PublicKey.IsValidSubjectPublicKeyInfo(spki));

        var ciphertext = Ecies.Encrypt(spki, plaintext);
        var decrypted = Ecies.Decrypt(recipient, ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Theory]
    [InlineData("p384")]
    [InlineData("p521")]
    public void Unsupported_ec_curves_are_rejected(string curveName)
    {
        var curve = curveName == "p384"
            ? ECCurve.NamedCurves.nistP384
            : ECCurve.NamedCurves.nistP521;
        using var key = ECDiffieHellman.Create(curve);
        var spki = key.ExportSubjectPublicKeyInfo();

        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(spki));
        Assert.Throws<CryptographicException>(() => Ecies.Encrypt(spki, "x"u8));
    }

    [Fact]
    public void Non_ec_malformed_truncated_empty_and_trailing_spki_are_rejected()
    {
        using var rsa = RSA.Create(2048);
        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var valid = p256.ExportSubjectPublicKeyInfo();
        var trailing = valid.Concat(new byte[] { 0x00 }).ToArray();
        var truncated = valid[..^1];

        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo()));
        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(new byte[] { 0x30, 0x01, 0x00 }));
        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(truncated));
        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(Array.Empty<byte>()));
        Assert.False(P256PublicKey.IsValidSubjectPublicKeyInfo(trailing));
    }

    [Fact]
    public async Task Anonymous_import_rejects_invalid_curve_before_enqueue_or_identity_persistence()
    {
        using var p384 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs/anonymous", new
        {
            apiKey = "must-not-reach-torn",
            publicKey = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        Assert.False(await db.IdentityMap.AnyAsync());

        var orchestrator = scope.ServiceProvider.GetRequiredService<ImportOrchestrator>();
        Assert.Null(orchestrator.Latest);
    }

    [Fact]
    public async Task Public_key_update_rejects_missing_identity_instead_of_reporting_success()
    {
        var anonymousId = Guid.NewGuid();
        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var client = _factory.CreateAuthenticatedClient(anonymousId.ToString());

        var response = await client.PutAsJsonAsync("/api/v1/identity/public-key", new
        {
            publicKey = Convert.ToBase64String(p256.ExportSubjectPublicKeyInfo()),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Public_key_update_persists_valid_p256_for_existing_owner()
    {
        var anonymousId = Guid.NewGuid();
        await _factory.SeedIdentityMapEntriesAsync(new IdentityMapEntity
        {
            AnonymousId = anonymousId,
            KeycloakSub = "test-sub",
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var spki = p256.ExportSubjectPublicKeyInfo();
        using var client = _factory.CreateAuthenticatedClient(anonymousId.ToString());

        var response = await client.PutAsJsonAsync("/api/v1/identity/public-key", new
        {
            publicKey = Convert.ToBase64String(spki),
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        var stored = await db.IdentityMap.SingleAsync(x => x.AnonymousId == anonymousId);
        Assert.Equal(spki, stored.PublicKey);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
