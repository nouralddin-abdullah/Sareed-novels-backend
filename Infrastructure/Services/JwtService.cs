using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

internal class JwtService(IConfiguration configuration, UserManager<User> userManager, TimeProvider time) : IJWTService
{
    /// <summary>How long an access token lives.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    public string GenerateAccessToken(User user)
    {
        // Get user roles
        var roles = userManager.GetRolesAsync(user).Result;

        var now = time.GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            // Issue time, for sign-out everywhere (ITokenRevocationService). Tokens from before it was added have none.
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim("DisplayName", user.DisplayName)
        };

        // Add role claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: now.Add(Lifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
