using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DrCare.Application;
using DrCare.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DrCare.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public AccessToken CreateAccessToken(User user)
    {
        var settings = options.Value;
        if (Encoding.UTF8.GetByteCount(settings.SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(settings.AccessTokenMinutes, 5, 60));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("org_id", user.OrganizationId.ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
