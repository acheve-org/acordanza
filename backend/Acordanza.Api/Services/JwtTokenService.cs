using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Acordanza.Api.Services;

public class JwtTokenService(IConfiguration configuration, UserManager<ApplicationUser> userManager, AppDbContext db)
{
    // Carries ApplicationUser.SecurityStamp into the token so JwtBearerEvents.OnTokenValidated
    // (Program.cs) can reject a token whose stamp no longer matches the user's current one —
    // ChangePasswordAsync/ResetPasswordAsync/ChangeEmailAsync all rotate the stamp already,
    // this is what makes that rotation actually revoke outstanding tokens (SEC-4a).
    public const string SecurityStampClaim = "security_stamp";

    public async Task<string> CreateToken(ApplicationUser user)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");
        var expiryMinutes = jwtSection.GetValue("ExpiryMinutes", 60 * 24 * 3);

        var roles = await userManager.GetRolesAsync(user);
        var (permissions, _, _) = await DynamicPermissions.ResolveAsync(db, roles, user.Id);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SecurityStampClaim, user.SecurityStamp ?? "")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

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
