using Plaat.Mobile.Services.Auth;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.IntegrationTests;

/// <summary>
/// Exercises Plaat.Mobile's login/refresh mechanics against a real running Plaat.Api - the same
/// login, token-issuance and refresh HTTP calls <c>AuthService</c>/<c>AccessTokenGate</c> make -
/// without needing the actual MAUI app or an emulator.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthFlowIntegrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Login_WithSeededAdminCredentials_IssuesAccessAndRefreshTokens()
    {
        using var factory = new PlaatClientTestFactory(fixture.Server.BaseUri);

        var token = await factory.Client.Login("admin", "Admin123!", clientIp: null, CancellationToken.None);

        token.ShouldNotBeNull();
        token.AccessToken.ShouldNotBeNullOrWhiteSpace();
        token.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        token.ExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Throws()
    {
        using var factory = new PlaatClientTestFactory(fixture.Server.BaseUri);

        await Should.ThrowAsync<HttpRequestException>(
            () => factory.Client.Login("admin", "wrong-password", clientIp: null, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentUser_AfterLogin_ReturnsAuthenticatedUser()
    {
        using var factory = new PlaatClientTestFactory(fixture.Server.BaseUri);
        var token = await factory.Client.Login("admin", "Admin123!", clientIp: null, CancellationToken.None);
        factory.TokenStorage.AccessToken = token!.AccessToken;
        factory.TokenStorage.RefreshToken = token.RefreshToken;
        factory.TokenStorage.ExpiresAt = token.ExpiresAt;

        var currentUser = await factory.Client.GetCurrentUser(CancellationToken.None);

        currentUser.ShouldNotBeNull();
        currentUser.Username.ShouldBe("admin");
    }

    [Fact]
    public async Task AccessTokenGate_TokenNearExpiry_RefreshesThroughRealApi()
    {
        using var factory = new PlaatClientTestFactory(fixture.Server.BaseUri);
        var token = await factory.Client.Login("admin", "Admin123!", clientIp: null, CancellationToken.None);

        // Simulate a token that is about to expire, forcing AccessTokenGate to refresh it.
        factory.TokenStorage.AccessToken = token!.AccessToken;
        factory.TokenStorage.RefreshToken = token.RefreshToken;
        factory.TokenStorage.ExpiresAt = DateTime.UtcNow.AddSeconds(10);

        var sessionStore = new InMemoryTokenSessionStore(factory.TokenStorage);
        var gate = new AccessTokenGate(factory.Client, factory.TokenStorage, sessionStore);

        var refreshedAccessToken = await gate.GetValidAccessToken(CancellationToken.None);

        refreshedAccessToken.ShouldNotBeNullOrWhiteSpace();
        factory.TokenStorage.ExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(5));

        // The server rotates the refresh token on every call (AuthEndpointMapper's
        // RotateRefreshToken) - that's the reliable signal a real refresh round-trip happened.
        // The access token JWT itself can legitimately come back byte-identical to the original
        // when both are issued within the same second, since its claims (incl. exp) are
        // second-granularity.
        factory.TokenStorage.RefreshToken.ShouldNotBe(token.RefreshToken);
    }
}
