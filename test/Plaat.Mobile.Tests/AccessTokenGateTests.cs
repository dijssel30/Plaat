using NSubstitute;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;
using Plaat.Mobile.Services.Auth;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.Tests;

public sealed class AccessTokenGateTests
{
    private static TokenResponse Response(string accessToken, DateTime expiresAt)
    {
        return new TokenResponse(accessToken, "refresh-token", expiresAt);
    }

    [Fact]
    public async Task GetValidAccessToken_TokenFarFromExpiry_DoesNotRefresh()
    {
        var plaatClient = Substitute.For<IPlaatClient>();
        var tokenStorage = new TokenStorageService
        {
            AccessToken = "still-fresh",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        var sessionStore = Substitute.For<ITokenSessionStore>();
        var gate = new AccessTokenGate(plaatClient, tokenStorage, sessionStore);

        var token = await gate.GetValidAccessToken(CancellationToken.None);

        token.ShouldBe("still-fresh");
        await plaatClient.DidNotReceive().RefreshToken(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetValidAccessToken_WithinRefreshMargin_RefreshesAndPersists()
    {
        var plaatClient = Substitute.For<IPlaatClient>();
        var tokenStorage = new TokenStorageService
        {
            AccessToken = "expiring-soon",
            RefreshToken = "old-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        var sessionStore = Substitute.For<ITokenSessionStore>();
        var refreshed = Response("new-access-token", DateTime.UtcNow.AddMinutes(30));
        plaatClient.RefreshToken("old-refresh-token", Arg.Any<CancellationToken>()).Returns(refreshed);

        var gate = new AccessTokenGate(plaatClient, tokenStorage, sessionStore);

        await gate.GetValidAccessToken(CancellationToken.None);

        await plaatClient.Received(1).RefreshToken("old-refresh-token", Arg.Any<CancellationToken>());
        await sessionStore.Received(1).PersistRefreshedToken(refreshed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetValidAccessToken_NoRefreshToken_ReturnsExistingAccessTokenWithoutCallingApi()
    {
        var plaatClient = Substitute.For<IPlaatClient>();
        var tokenStorage = new TokenStorageService
        {
            AccessToken = "some-token",
            RefreshToken = null,
            ExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        var sessionStore = Substitute.For<ITokenSessionStore>();
        var gate = new AccessTokenGate(plaatClient, tokenStorage, sessionStore);

        var token = await gate.GetValidAccessToken(CancellationToken.None);

        token.ShouldBe("some-token");
        await plaatClient.DidNotReceive().RefreshToken(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetValidAccessToken_RefreshThrows_FallsBackToStaleTokenWithoutThrowing()
    {
        var plaatClient = Substitute.For<IPlaatClient>();
        var tokenStorage = new TokenStorageService
        {
            AccessToken = "stale-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        var sessionStore = Substitute.For<ITokenSessionStore>();
        plaatClient.RefreshToken(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TokenResponse?>(new HttpRequestException("network down")));

        var gate = new AccessTokenGate(plaatClient, tokenStorage, sessionStore);

        var token = await gate.GetValidAccessToken(CancellationToken.None);

        token.ShouldBe("stale-token");
    }
}
