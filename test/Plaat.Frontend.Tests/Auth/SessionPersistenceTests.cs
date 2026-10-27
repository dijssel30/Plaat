using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Auth;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Auth;

/// <summary>
/// Restoring, refreshing and clearing a signed-in session across circuit reconnects: the encrypted
/// browser-storage round trip, the auth-state notifications and the proxy-session sync.
/// </summary>
public sealed class SessionPersistenceTests
{
    private readonly FakeLocalStorageRuntime _browser = new();
    private readonly TokenStorageService _tokens = new();
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly IProxySessionSync _proxy = Substitute.For<IProxySessionSync>();
    private readonly PersistentTokenStore _store;
    private readonly JwtAuthStateProvider _authState;
    private readonly AuthService _auth;
    private readonly List<ClaimsPrincipal> _notifiedUsers = [];

    public SessionPersistenceTests()
    {
        _store = new PersistentTokenStore(
            new ProtectedLocalStorage(_browser, new EphemeralDataProtectionProvider()),
            NullLogger<PersistentTokenStore>.Instance);
        _authState = new JwtAuthStateProvider(_tokens);
        _authState.AuthenticationStateChanged += async task => _notifiedUsers.Add((await task).User);
        var sessionStore = new TokenSessionStore(_tokens, _store, _authState, _proxy);
        _auth = new AuthService(_client, _tokens, _store, sessionStore, _proxy, _authState);
    }

    private static string Jwt(string username)
    {
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: [new Claim("name", username)]));
    }

    private static TokenResponse Token(string username, TimeSpan expiresIn, string refresh = "refresh")
    {
        return new TokenResponse(Jwt(username), refresh, DateTime.UtcNow.Add(expiresIn));
    }

    // ---- PersistentTokenStore ----

    [Fact]
    public async Task Store_SaveLoadClear_RoundTripsThroughEncryptedStorage()
    {
        var token = Token("alice", TimeSpan.FromMinutes(5));

        await _store.Save(token);

        _browser.Items.Values.Single().ShouldNotContain(token.RefreshToken);
        (await _store.Load()).ShouldBe(token);

        await _store.Clear();

        (await _store.Load()).ShouldBeNull();
    }

    [Fact]
    public async Task Store_WhenBrowserStorageFails_SwallowsErrors()
    {
        _browser.Broken = true;

        await Should.NotThrowAsync(() => _store.Save(Token("alice", TimeSpan.FromMinutes(5))));
        (await _store.Load()).ShouldBeNull();
        await Should.NotThrowAsync(() => _store.Clear());
    }

    // ---- RestoreSession ----

    [Fact]
    public async Task RestoreSession_AlreadySignedInThisCircuit_OnlyResyncsTheProxySession()
    {
        var token = Token("alice", TimeSpan.FromMinutes(5));
        _tokens.AccessToken = token.AccessToken;
        _tokens.ExpiresAt = token.ExpiresAt;

        await _auth.RestoreSession(CancellationToken.None);

        await _proxy.Received(1).Establish(token.AccessToken, token.ExpiresAt);
        _browser.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreSession_NothingStored_StaysSignedOut()
    {
        await _auth.RestoreSession(CancellationToken.None);

        _tokens.IsAuthenticated.ShouldBeFalse();
        _proxy.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreSession_StoredTokenStillValid_SignsBackInWithoutRefreshing()
    {
        var token = Token("alice", TimeSpan.FromMinutes(5));
        await _store.Save(token);

        await _auth.RestoreSession(CancellationToken.None);

        _tokens.AccessToken.ShouldBe(token.AccessToken);
        _notifiedUsers.ShouldHaveSingleItem().FindFirst("name")!.Value.ShouldBe("alice");
        await _proxy.Received(1).Establish(token.AccessToken, token.ExpiresAt);
        await _client.DidNotReceiveWithAnyArgs().RefreshToken(null!, CancellationToken.None);
    }

    [Fact]
    public async Task RestoreSession_StoredTokenExpired_RefreshesAndPersistsTheNewPair()
    {
        await _store.Save(Token("alice", TimeSpan.FromMinutes(-5), refresh: "old-refresh"));
        var fresh = Token("alice", TimeSpan.FromMinutes(30), refresh: "new-refresh");
        _client.RefreshToken("old-refresh", Arg.Any<CancellationToken>()).Returns(fresh);

        await _auth.RestoreSession(CancellationToken.None);

        _tokens.RefreshToken.ShouldBe("new-refresh");
        (await _store.Load()).ShouldBe(fresh);
        await _proxy.Received(1).Establish(fresh.AccessToken, fresh.ExpiresAt);
    }

    [Fact]
    public async Task RestoreSession_RefreshRejected_SignsOutCompletely()
    {
        await _store.Save(Token("alice", TimeSpan.FromMinutes(-5), refresh: "revoked"));
        _client.RefreshToken("revoked", Arg.Any<CancellationToken>()).Returns((TokenResponse?)null);

        await _auth.RestoreSession(CancellationToken.None);

        _tokens.AccessToken.ShouldBeNull();
        (await _store.Load()).ShouldBeNull();
        _notifiedUsers.ShouldHaveSingleItem().Identity!.IsAuthenticated.ShouldBeFalse();
        await _proxy.Received(1).Clear();
    }

    [Fact]
    public async Task RestoreSession_RefreshFails_SignsOut()
    {
        await _store.Save(Token("alice", TimeSpan.FromMinutes(-5), refresh: "boom"));
        _client.RefreshToken("boom", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TokenResponse?>(new HttpRequestException("api down")));

        await _auth.RestoreSession(CancellationToken.None);

        (await _store.Load()).ShouldBeNull();
        await _proxy.Received(1).Clear();
    }

    [Fact]
    public async Task RestoreSession_StoredTokenWithoutRefreshToken_SignsOut()
    {
        await _store.Save(new TokenResponse(Jwt("alice"), null!, DateTime.UtcNow.AddMinutes(-5)));

        await _auth.RestoreSession(CancellationToken.None);

        await _client.DidNotReceiveWithAnyArgs().RefreshToken(null!, CancellationToken.None);
        await _proxy.Received(1).Clear();
    }

    // ---- JwtAuthStateProvider ----

    [Fact]
    public async Task AuthState_ReflectsTheTokenClaimsOrIsAnonymous()
    {
        (await _authState.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated.ShouldBeFalse();

        _tokens.AccessToken = Jwt("bob");
        _tokens.ExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var user = (await _authState.GetAuthenticationStateAsync()).User;
        user.Identity!.IsAuthenticated.ShouldBeTrue();
        user.FindFirst("name")!.Value.ShouldBe("bob");
    }

    [Fact]
    public void NotifyUserAuthenticated_WithoutAToken_Throws()
    {
        Should.Throw<UnauthorizedAccessException>(() => _authState.NotifyUserAuthenticated());
    }

    // ---- ProxySessionSync ----

    [Fact]
    public async Task ProxySessionSync_CallsTheBrowserAndToleratesMissingInterop()
    {
        var runtime = new FakeLocalStorageRuntime();
        var sync = new ProxySessionSync(runtime, NullLogger<ProxySessionSync>.Instance);
        var expires = DateTime.UtcNow;

        await sync.Establish("token", expires);
        await sync.Clear();

        runtime.Calls.ShouldBe(["plaatSession.establish", "plaatSession.clear"]);

        runtime.ThrowOnInvoke = new JSDisconnectedException("gone");
        await Should.NotThrowAsync(() => sync.Clear());
        runtime.ThrowOnInvoke = new InvalidOperationException("prerendering");
        await Should.NotThrowAsync(() => sync.Establish("token", expires));
    }

    // ---- ProxySessionStore ----

    [Fact]
    public void ProxySessionStore_IssuesReusesExpiresAndRemovesSessions()
    {
        var sessions = new ProxySessionStore();

        var id = sessions.Establish(null, "t1", DateTime.UtcNow.AddMinutes(5));
        sessions.GetAccessToken(id).ShouldBe("t1");
        id.ShouldNotContain("=");

        sessions.Establish(id, "t2", DateTime.UtcNow.AddMinutes(5)).ShouldBe(id);
        sessions.GetAccessToken(id).ShouldBe("t2");
        sessions.Establish("unknown-id", "t3", DateTime.UtcNow.AddMinutes(5)).ShouldNotBe("unknown-id");

        var expired = sessions.Establish(null, "old", DateTime.UtcNow.AddMinutes(-1));
        sessions.GetAccessToken(expired).ShouldBeNull();
        sessions.GetAccessToken(expired).ShouldBeNull();

        sessions.Remove(id);
        sessions.GetAccessToken(id).ShouldBeNull();
    }

    /// <summary>
    /// Minimal browser: an in-memory localStorage behind the JS interop calls ProtectedLocalStorage makes.
    /// </summary>
    private sealed class FakeLocalStorageRuntime : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = [];

        public List<string> Calls { get; } = [];

        public bool Broken { get; set; }

        public Exception? ThrowOnInvoke { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (ThrowOnInvoke is not null)
            {
                throw ThrowOnInvoke;
            }

            if (Broken)
            {
                throw new JSException("storage unavailable");
            }

            Calls.Add(identifier);
            var key = args is { Length: > 0 } ? args[0] as string : null;

            switch (identifier)
            {
                case "localStorage.setItem":
                    Items[key!] = (string)args![1]!;
                    break;
                case "localStorage.removeItem":
                    Items.Remove(key!);
                    break;
                case "localStorage.getItem":
                    return ValueTask.FromResult((TValue)(object?)Items.GetValueOrDefault(key!)!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }
}
