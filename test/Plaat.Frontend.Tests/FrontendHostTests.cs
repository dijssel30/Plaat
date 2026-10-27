using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests;

/// <summary>
/// The Frontend's own HTTP endpoints, through its real pipeline with a stubbed upstream API: the
/// cookie session that only trusts tokens the API accepts, and the media proxy that attaches the
/// session's token to cover art, audio and Discogs image requests.
/// </summary>
public sealed class FrontendHostTests : IClassFixture<FrontendHostTests.FrontendFactory>
{
    private readonly FrontendFactory _factory;

    public FrontendHostTests(FrontendFactory factory)
    {
        _factory = factory;
        _factory.Upstream.Reset();
    }

    public sealed class FrontendFactory : WebApplicationFactory<Program>
    {
        public StubUpstream Upstream { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddHttpClient("proxy").ConfigurePrimaryHttpMessageHandler(() => Upstream);
            });
        }
    }

    /// <summary>
    /// Plays the API: records requests and answers with a scripted response.
    /// </summary>
    public sealed class StubUpstream : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public void Reset()
        {
            Requests.Clear();
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(Respond(request));
        }
    }

    private HttpClient Client()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    }

    private static string Jwt()
    {
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: [new Claim("sub", "1"), new Claim("role", "Admin")]));
    }

    private string SessionFor(string accessToken)
    {
        return _factory.Services.GetRequiredService<ProxySessionStore>().Establish(null, accessToken, DateTime.UtcNow.AddMinutes(5));
    }

    private static HttpRequestMessage Get(string path, string? sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (sessionId is not null)
        {
            request.Headers.Add("Cookie", $"{SessionEndpoints.CookieName}={sessionId}");
        }

        return request;
    }

    // ---- proxy ----

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-session")]
    public async Task Proxy_WithoutAKnownSession_Is401WithoutCallingTheApi(string? sessionId)
    {
        using var response = await Client().SendAsync(Get("/proxy/cover/album/1", sessionId));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _factory.Upstream.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/proxy/cover/album/7", "/api/cover/album/7")]
    [InlineData("/proxy/cover/track/8", "/api/cover/track/8")]
    [InlineData("/proxy/discogs-image/artist/9", "/api/artists/9/discogs-image")]
    public async Task Proxy_ForwardsWithTheSessionsTokenAndCopiesTheResponse(string path, string upstreamPath)
    {
        var session = SessionFor("token-abc");
        _factory.Upstream.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
            response.Headers.ETag = new EntityTagHeaderValue("\"etag-1\"");
            response.Headers.CacheControl = new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromDays(1) };

            return response;
        };

        using var response = await Client().SendAsync(Get(path, session));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        response.Content.Headers.ContentLength.ShouldBe(3);
        response.Headers.ETag!.Tag.ShouldBe("\"etag-1\"");
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(new byte[] { 1, 2, 3 });
        var upstream = _factory.Upstream.Requests.ShouldHaveSingleItem();
        upstream.RequestUri!.AbsolutePath.ShouldBe(upstreamPath);
        upstream.Headers.Authorization!.ToString().ShouldBe("Bearer token-abc");
        upstream.Headers.Contains("Range").ShouldBeFalse();
    }

    [Fact]
    public async Task Proxy_CoverRevalidation_PassesIfNoneMatchAndReturnsA304()
    {
        var session = SessionFor("token-abc");
        _factory.Upstream.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.ETag = new EntityTagHeaderValue("\"etag-1\"");

            return response;
        };
        using var request = Get("/proxy/cover/album/7", session);
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"etag-1\"");

        using var response = await Client().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        response.Headers.ETag!.Tag.ShouldBe("\"etag-1\"");
        _factory.Upstream.Requests.Single().Headers.GetValues("If-None-Match").Single().ShouldBe("\"etag-1\"");
    }

    [Fact]
    public async Task Proxy_AudioRangeRequest_ForwardsTheRangeAndThePartialResponseHeaders()
    {
        var session = SessionFor("token-abc");
        _factory.Upstream.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([5, 6]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, 6, 10);
            response.Headers.AcceptRanges.Add("bytes");

            return response;
        };
        using var request = Get("/proxy/music/3", session);
        request.Headers.TryAddWithoutValidation("Range", "bytes=5-6");

        using var response = await Client().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.ToString().ShouldBe("bytes 5-6/10");
        response.Headers.AcceptRanges.ShouldContain("bytes");
        _factory.Upstream.Requests.Single().Headers.GetValues("Range").Single().ShouldBe("bytes=5-6");
        _factory.Upstream.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/api/music/3");
    }

    [Fact]
    public async Task Proxy_AudioWithoutRange_StreamsTheWholeFile()
    {
        var session = SessionFor("token-abc");
        _factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };

        using var response = await Client().SendAsync(Get("/proxy/music/3", session));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Upstream.Requests.Single().Headers.Contains("Range").ShouldBeFalse();
        response.Headers.AcceptRanges.ShouldBeEmpty();
    }

    [Fact]
    public async Task Proxy_UpstreamErrors_ArePassedThrough()
    {
        var session = SessionFor("token-abc");
        _factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        using var response = await Client().SendAsync(Get("/proxy/music/404", session));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- session ----

    private static HttpRequestMessage Establish(string token, string? existingSessionId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/session/establish")
        {
            Content = JsonContent.Create(new EstablishSessionRequest(token, DateTime.UtcNow.AddMinutes(30)))
        };
        if (existingSessionId is not null)
        {
            request.Headers.Add("Cookie", $"{SessionEndpoints.CookieName}={existingSessionId}");
        }

        return request;
    }

    private static string? SessionCookie(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith($"{SessionEndpoints.CookieName}=", StringComparison.Ordinal))
            : null;
    }

    [Fact]
    public async Task Establish_WithoutAToken_IsABadRequest()
    {
        using var response = await Client().SendAsync(Establish(""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _factory.Upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Establish_TokenTheApiRejects_IsUnauthorizedAndSetsNoCookie()
    {
        _factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var response = await Client().SendAsync(Establish(Jwt()));

        // Rejected (the 401 is re-executed through the status-code page, which answers a POST
        // with 400) - what matters is that no session is handed out.
        response.IsSuccessStatusCode.ShouldBeFalse();
        SessionCookie(response).ShouldBeNull();
        _factory.Upstream.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/api/state");
    }

    [Fact]
    public async Task Establish_ApiUnreachable_IsUnauthorized()
    {
        _factory.Upstream.Respond = _ => throw new HttpRequestException("connection refused");

        using var response = await Client().SendAsync(Establish(Jwt()));

        response.IsSuccessStatusCode.ShouldBeFalse();
        SessionCookie(response).ShouldBeNull();
    }

    [Fact]
    public async Task Establish_AcceptedToken_SetsASecureSessionCookieThatUnlocksTheProxy()
    {
        var token = Jwt();

        using var response = await Client().SendAsync(Establish(token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = SessionCookie(response).ShouldNotBeNull();
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        _factory.Upstream.Requests.Single().Headers.Authorization!.Parameter.ShouldBe(token);

        var sessionId = cookie.Split(';')[0].Split('=', 2)[1];
        _factory.Services.GetRequiredService<ProxySessionStore>().GetAccessToken(sessionId).ShouldBe(token);

        using var again = await Client().SendAsync(Establish(token, sessionId));
        SessionCookie(again)!.ShouldStartWith($"{SessionEndpoints.CookieName}={sessionId};");
    }

    [Fact]
    public async Task Clear_EndsTheSession()
    {
        var session = SessionFor("token-to-clear");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/session/clear");
        request.Headers.Add("Cookie", $"{SessionEndpoints.CookieName}={session}");

        using var response = await Client().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Services.GetRequiredService<ProxySessionStore>().GetAccessToken(session).ShouldBeNull();
        SessionCookie(response)!.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
    }

    [Fact]
    public async Task Clear_WithoutASession_StillSucceeds()
    {
        using var response = await Client().PostAsync("/session/clear", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_CarryTheSecurityHeaders()
    {
        using var response = await Client().PostAsync("/session/clear", null);

        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
    }
}
