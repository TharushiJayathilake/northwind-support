using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Authentication;

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, AuthAgent Agent);

public sealed class JwtTokens(JwtSettings settings, IClock clock)
{
    public LoginResponse Create(AuthAgent agent)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
             new Claim("name", agent.FullName)], now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, agent);
    }
}
