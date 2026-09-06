using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HappyGymStats.Identity.Provisional;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ProvisionalTokenServiceTests
{
    private const string SigningKey = "test-only-provisional-token-signing-key-32-bytes-minimum";

    [Fact]
    public void Issued_token_round_trips_the_anonymous_identity()
    {
        var service = CreateService();
        var anonymousId = Guid.NewGuid();

        var token = service.Issue(anonymousId);

        Assert.Equal(anonymousId, service.Validate(token));
    }

    [Fact]
    public void Validation_rejects_expired_tampered_wrong_type_and_malformed_tokens()
    {
        var service = CreateService();
        var anonymousId = Guid.NewGuid();
        var issued = service.Issue(anonymousId);
        var segments = issued.Split('.');
        var changedSignature = $"{(segments[2][0] == 'A' ? 'B' : 'A')}{segments[2][1..]}";
        var tampered = $"{segments[0]}.{segments[1]}.{changedSignature}";

        Assert.Null(service.Validate(CreateToken(anonymousId, "provisional", DateTime.UtcNow.AddMinutes(-1))));
        Assert.Null(service.Validate(tampered));
        Assert.Null(service.Validate(CreateToken(anonymousId, "different-purpose", DateTime.UtcNow.AddHours(1))));
        Assert.Null(service.Validate("not-a-jwt"));
    }

    private static ProvisionalTokenService CreateService() =>
        new(Options.Create(new ProvisionalTokenOptions
        {
            SigningKey = SigningKey,
            ExpiryHours = 1,
        }));

    private static string CreateToken(Guid anonymousId, string tokenType, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, anonymousId.ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, tokenType),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        }.WriteToken(token);
    }
}
