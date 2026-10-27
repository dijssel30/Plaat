using System.Net;
using Bunit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Auth;
using Plaat.Frontend.Components.Pages;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class LoginComponentTests : BunitContext
{
    private readonly IPlaatClient _plaatClient = Substitute.For<IPlaatClient>();
    private readonly TokenStorageService _tokenStorage = new();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    public LoginComponentTests()
    {
        var authStateProvider = new JwtAuthStateProvider(_tokenStorage);

        Services.AddSingleton(new AuthService(
            _plaatClient,
            _tokenStorage,
            null!,
            Substitute.For<ITokenSessionStore>(),
            Substitute.For<IProxySessionSync>(),
            authStateProvider));
        Services.AddSingleton<AuthenticationStateProvider>(authStateProvider);
        Services.AddSingleton(_httpContextAccessor);
    }

    private void GivenLoginSucceeds()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TokenResponse("access", "refresh", DateTime.UtcNow.AddMinutes(30)));
    }

    private IRenderedComponent<Login> RenderAt(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);

        return Render<Login>();
    }

    private async Task SignIn(IRenderedComponent<Login> cut)
    {
        cut.Find("#username").Change("admin");
        cut.Find("#password").Change("pw");

        await cut.Find("button.login-btn").ClickAsync(new());
    }

    [Fact]
    public async Task SignIn_TooManyRequests_ShowsTooManyAttemptsMessage()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("limited", null, HttpStatusCode.TooManyRequests));
        var cut = Render<Login>();

        await SignIn(cut);

        cut.Find(".login-error").TextContent.ShouldContain("Too many login attempts");
    }

    [Fact]
    public async Task SignIn_WrongPassword_ShowsInvalidCredentialsMessage()
    {
        _plaatClient.Login(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("nope", null, HttpStatusCode.Unauthorized));
        var cut = Render<Login>();

        await SignIn(cut);

        cut.Find(".login-error").TextContent.ShouldBe("Invalid username or password");
    }

    [Theory]
    [InlineData("/login", "/")]
    [InlineData("/login?returnUrl=%2Falbums%2F3", "/albums/3")]
    [InlineData("/login?returnUrl=", "/")]
    [InlineData("/login?returnUrl=https%3A%2F%2Fevil.example", "/")]
    [InlineData("/login?returnUrl=%2F%2Fevil.example", "/")]
    [InlineData("/login?returnUrl=%2F%5Cevil.example", "/")]
    public async Task SignIn_Success_OnlyEverReturnsToALocalPath(string loginUrl, string expectedPath)
    {
        GivenLoginSucceeds();
        var cut = RenderAt(loginUrl);

        await SignIn(cut);

        var navigation = Services.GetRequiredService<NavigationManager>();
        new Uri(navigation.Uri).PathAndQuery.ShouldBe(expectedPath);
    }

    [Fact]
    public async Task SignIn_WithoutUsernameOrPassword_ShowsAnErrorWithoutCallingTheApi()
    {
        var cut = Render<Login>();

        await cut.Find("button.login-btn").ClickAsync(new());

        cut.Find(".login-error").TextContent.ShouldBe("Invalid username or password");
        await _plaatClient.DidNotReceiveWithAnyArgs().Login(null!, null!, null, CancellationToken.None);
    }

    [Fact]
    public async Task SignIn_PassesTheBrowsersIpToTheApi()
    {
        GivenLoginSucceeds();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        _httpContextAccessor.HttpContext.Returns(httpContext);
        var cut = Render<Login>();

        await SignIn(cut);

        await _plaatClient.Received(1).Login("admin", "pw", "10.1.2.3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AlreadySignedIn_GoesStraightToTheReturnUrl()
    {
        _tokenStorage.AccessToken = new JwtSecurityTokenHandler()
            .WriteToken(new JwtSecurityToken(claims: [new Claim("sub", "1")]));
        _tokenStorage.ExpiresAt = DateTime.UtcNow.AddMinutes(5);

        RenderAt("/login?returnUrl=%2Fstats");

        var navigation = Services.GetRequiredService<NavigationManager>();
        new Uri(navigation.Uri).PathAndQuery.ShouldBe("/stats");
    }
}
