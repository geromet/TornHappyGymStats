using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HappyGymStats.Identity.Provisional;

public sealed class ProvisionalTokenService : IProvisionalTokenService
{
    private const string TokenType = "provisional";
    // These are app-owned tokens whose validation contract uses the registered JWT
    // claim names below. Keep that policy local so it cannot change Keycloak claims.
    private readonly JwtSecurityTokenHandler _handler = new()
    {
        MapInboundClaims = false,
    };
    private readonly ProvisionalTokenOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SymmetricSecurityKey _signingKey;

    public ProvisionalTokenService(
        IOptions<ProvisionalTokenOptions> options,
        TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.ExpiryHours <= 0)
            throw new InvalidOperationException("Provisional token expiry must be greater than zero hours.");

        var signingKeyBytes = Encoding.UTF8.GetBytes(_options.SigningKey ?? string.Empty);
        if (signingKeyBytes.Length < 32)
            throw new InvalidOperationException("Provisional token signing key must be at least 32 bytes.");

        _signingKey = new SymmetricSecurityKey(signingKeyBytes);
    }

    public IssuedProvisionalToken Issue(Guid anonymousId)
    {
        // JWT NumericDate has whole-second precision. Return that exact value so
        // the persisted row and bearer token share one expiry boundary.
        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(
            _timeProvider.GetUtcNow().AddHours(_options.ExpiryHours).ToUnixTimeSeconds());
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, anonymousId.ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, TokenType),
            ],
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new IssuedProvisionalToken(_handler.WriteToken(token), expiresAtUtc);
    }

    public Guid? Validate(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    return expires is not null
                           && expires > now
                           && (notBefore is null || notBefore <= now);
                },
            }, out _);

            var typ = principal.FindFirstValue(JwtRegisteredClaimNames.Typ);
            if (typ != TokenType) return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}
