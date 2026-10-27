using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Plaat.Biz.Options;
using Plaat.Biz.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class TokenServiceTests
{
    private static PlaatOptions BuildOptions()
    {
        return new()
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/converted",
            JwtOptions = new JwtOptions { Key = "super-secret-test-signing-key-0123456789" }
        };
    }

    [Fact]
    public async Task GenerateAccessToken_UnknownUser_Throws()
    {
        using var fixture = new BizTestFixture();
        var service = new TokenService(fixture.DbFactory, BuildOptions());

        var exception = await Record.ExceptionAsync(() =>
            service.GenerateAccessToken("nobody", CancellationToken.None));

        exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task GenerateAccessToken_KnownUser_ContainsExpectedClaims()
    {
        using var fixture = new BizTestFixture();
        var user = fixture.SeedUser("alice");
        var service = new TokenService(fixture.DbFactory, BuildOptions());

        var token = await service.GenerateAccessToken("alice", CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldContain(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwt.Claims.ShouldContain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "alice");
        jwt.Claims.ShouldNotContain(c => c.Type == ClaimTypes.Role);
        jwt.Issuer.ShouldBe(JwtOptions.Issuer);
        jwt.Audiences.ShouldContain(JwtOptions.Audience);
    }

    [Fact]
    public async Task GenerateAccessToken_AdminUser_ContainsAdminRoleClaim()
    {
        using var fixture = new BizTestFixture();
        fixture.SeedUser("boss", isAdmin: true);
        var service = new TokenService(fixture.DbFactory, BuildOptions());

        var token = await service.GenerateAccessToken("boss", CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldContain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void GetAccessTokenExpiry_ReturnsApproximatelyThirtyMinutesFromNow()
    {
        var before = DateTime.UtcNow;

        var expiry = TokenService.GetAccessTokenExpiry();

        var after = DateTime.UtcNow;
        expiry.ShouldBeInRange(before.AddMinutes(30), after.AddMinutes(30).AddSeconds(1));
    }
}
