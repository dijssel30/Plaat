using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Plaat.Biz.Options;
using Plaat.Db;

namespace Plaat.Biz;

public sealed class TokenService(
    IDbContextFactory<PlaatContext> contextFactory,
    PlaatOptions plaatOptions)
{
    public async Task<string> GenerateAccessToken(string username, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            throw new Exception("User not found");
        }

        var jwtOptions = plaatOptions.JwtOptions;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = GetAccessTokenExpiry();

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString(new CultureInfo("nl-NL"))),
            new(ClaimTypes.NameIdentifier, user.Username)
        };

        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var token = new JwtSecurityToken(
            issuer: JwtOptions.Issuer,
            audience: JwtOptions.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiry,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static DateTime GetAccessTokenExpiry()
    {
        return DateTime.UtcNow.AddMinutes(30);
    }
}
