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
    private static readonly DateTimeOffset Now =
        new(2030, 6, 15, 12, 30, 45, TimeSpan.Zero);

    [Fact]
    public void Issued_token_round_trips_the_anonymous_identity()
    {
        var clock = new MutableTimeProvider(Now.AddMilliseconds(987));
        var service = CreateService(clock);
        var anonymousId = Guid.NewGuid();

        var issued = service.Issue(anonymousId);

        Assert.Equal(Now.AddHours(1), issued.ExpiresAtUtc);
        Assert.Equal(anonymousId, service.Validate(issued.Value));

        clock.UtcNow = issued.ExpiresAtUtc.AddTicks(-1);
        Assert.Equal(anonymousId, service.Validate(issued.Value));

        clock.UtcNow = issued.ExpiresAtUtc;
        Assert.Null(service.Validate(issued.Value));
    }

    [Fact]
    public void Validation_rejects_expired_tampered_wrong_type_and_malformed_tokens()
    {
        var service = CreateService(new MutableTimeProvider(Now));
        var anonymousId = Guid.NewGuid();
        var issued = service.Issue(anonymousId).Value;
        var segments = issued.Split('.');
        var changedSignature = $"{(segments[2][0] == 'A' ? 'B' : 'A')}{segments[2][1..]}";
        var tampered = $"{segments[0]}.{segments[1]}.{changedSignature}";

        Assert.Null(service.Validate(CreateToken(anonymousId, "provisional", Now.AddMinutes(-1).UtcDateTime)));
        Assert.Null(service.Validate(tampered));
        Assert.Null(service.Validate(CreateToken(anonymousId, "different-purpose", Now.AddHours(1).UtcDateTime)));
        Assert.Null(service.Validate("not-a-jwt"));
    }

    [Theory]
    [InlineData("short", 1)]
    [InlineData(SigningKey, 0)]
    [InlineData(SigningKey, -1)]
    public void Invalid_configuration_is_rejected_before_token_issuance(
        string signingKey,
        int expiryHours)
    {
        Assert.Throws<InvalidOperationException>(() => new ProvisionalTokenService(
            Options.Create(new ProvisionalTokenOptions
            {
                SigningKey = signingKey,
                ExpiryHours = expiryHours,
            }),
            new MutableTimeProvider(Now)));
    }

    private static ProvisionalTokenService CreateService(TimeProvider clock) =>
        new(Options.Create(new ProvisionalTokenOptions
        {
            SigningKey = SigningKey,
            ExpiryHours = 1,
        }), clock);

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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
