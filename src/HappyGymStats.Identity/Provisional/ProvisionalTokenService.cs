using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HappyGymStats.Identity.Provisional;

public sealed class ProvisionalTokenService : IProvisionalTokenService
{
    private const string TokenType = "provisional";
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly ProvisionalTokenOptions _options;

    public ProvisionalTokenService(IOptions<ProvisionalTokenOptions> options)
    {
        _options = options.Value;
    }

    public string Issue(Guid anonymousId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, anonymousId.ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, TokenType),
            ],
            expires: DateTime.UtcNow.AddHours(_options.ExpiryHours),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(token);
    }

    public Guid? Validate(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero,
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
