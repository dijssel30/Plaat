using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Auth;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Auth;

public sealed class AuthServiceLoginTests
{
    private static readonly TokenResponse Token = new("access", "refresh", DateTime.UtcNow.AddMinutes(30));

    private readonly IPlaatClient _plaatClient = Substitute.For<IPlaatClient>();
    private readonly ITokenSessionStore _tokenSessionStore = Substitute.For<ITokenSessionStore>();
    private readonly AuthService _authService;

    public AuthServiceLoginTests()
    {
        var tokenStorage = new TokenStorageService();

        // Login never touches the persistent token store or the proxy session sync.
        _authService = new AuthService(
            _plaatClient,
            tokenStorage,
            null!,
            _tokenSessionStore,
            Substitute.For<IProxySessionSync>(),
            new JwtAuthStateProvider(tokenStorage));
    }

    [Fact]
    public async Task Login_ValidCredentials_PersistsTokenAndReturnsSuccess()
    {
        _plaatClient.Login("admin", "pw", "1.2.3.4", Arg.Any<CancellationToken>()).Returns(Token);

        var result = await _authService.Login("admin", "pw", "1.2.3.4", CancellationToken.None);

        result.ShouldBe(LoginResult.Success);
        await _tokenSessionStore.Received(1).PersistRefreshedToken(Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_NoTokenReturned_ReturnsInvalidCredentials()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((TokenResponse?)null);

        var result = await _authService.Login("admin", "pw", null, CancellationToken.None);

        result.ShouldBe(LoginResult.InvalidCredentials);
        await _tokenSessionStore.DidNotReceiveWithAnyArgs().PersistRefreshedToken(default!, default);
    }

    [Fact]
    public async Task Login_Unauthorized_ReturnsInvalidCredentials()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("nope", null, HttpStatusCode.Unauthorized));

        var result = await _authService.Login("admin", "wrong", null, CancellationToken.None);

        result.ShouldBe(LoginResult.InvalidCredentials);
    }

    [Fact]
    public async Task Login_TooManyRequests_ReturnsTooManyAttempts()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("limited", null, HttpStatusCode.TooManyRequests));

        var result = await _authService.Login("admin", "pw", null, CancellationToken.None);

        result.ShouldBe(LoginResult.TooManyAttempts);
        await _tokenSessionStore.DidNotReceiveWithAnyArgs().PersistRefreshedToken(default!, default);
    }

    [Fact]
    public async Task Login_UnexpectedFailure_ReturnsInvalidCredentials()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));

        var result = await _authService.Login("admin", "pw", null, CancellationToken.None);

        result.ShouldBe(LoginResult.InvalidCredentials);
    }
}
