using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Acordanza.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Acordanza.Api.Services;

public class JwtTokenService(IConfiguration configuration)
{
    public string CreateToken(ApplicationUser user)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");
        var expiryMinutes = jwtSection.GetValue("ExpiryMinutes", 60 * 24 * 7);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
