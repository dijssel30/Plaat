using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plaat.Contract;
using Plaat.Mobile.Services.Auth;
using Plaat.Mobile.Services.Streaming;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.IntegrationTests;

/// <summary>
/// Drives the real <see cref="LoopbackProxyService"/> (an HttpListener, exactly as it runs inside
/// the MAUI app) against a real running Plaat.Api, verifying it actually forwards bearer auth,
/// Range requests and conditional-caching headers correctly - the whole reason this proxy exists
/// (native MediaElement/Image controls can't attach an Authorization header themselves).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LoopbackProxyServiceIntegrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private PlaatClientTestFactory _clientFactory = null!;
    private LoopbackProxyService _proxy = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        _clientFactory = new PlaatClientTestFactory(fixture.Server.BaseUri);
        var token = await _clientFactory.Client.Login("admin", "Admin123!", clientIp: null, CancellationToken.None);
        _clientFactory.TokenStorage.AccessToken = token!.AccessToken;
        _clientFactory.TokenStorage.RefreshToken = token.RefreshToken;
        _clientFactory.TokenStorage.ExpiresAt = token.ExpiresAt;

        var services = new ServiceCollection();
        services.AddHttpClient();
        _provider = services.BuildServiceProvider();

        var sessionStore = new InMemoryTokenSessionStore(_clientFactory.TokenStorage);
        var accessTokenGate = new AccessTokenGate(_clientFactory.Client, _clientFactory.TokenStorage, sessionStore);

        _proxy = new LoopbackProxyService(
            _provider.GetRequiredService<IHttpClientFactory>(),
            new PlaatBackendOptions { BaseUri = fixture.Server.BaseUri },
            accessTokenGate,
            NullLogger<LoopbackProxyService>.Instance);
        _proxy.Start();

        _httpClient = new HttpClient();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        _provider.Dispose();
        _clientFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MusicUrl_StreamsFullTrackWithBearerInjected()
    {
        var response = await _httpClient.GetAsync(_proxy.MusicUrl(fixture.SeededTrackId));

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("audio/wav");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task MusicUrl_WithRangeHeader_ForwardsPartialContent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _proxy.MusicUrl(fixture.SeededTrackId));
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);

        var response = await _httpClient.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange.ShouldNotBeNull();
        (await response.Content.ReadAsByteArrayAsync()).Length.ShouldBe(100);
    }

    [Fact]
    public async Task AlbumCoverUrl_UnknownCover_ReturnsDefaultImageWithEtag()
    {
        var response = await _httpClient.GetAsync(_proxy.AlbumCoverUrl(fixture.SeededAlbumId));

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        response.Headers.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task TrackCoverUrl_ConditionalRequestWithMatchingEtag_Returns304()
    {
        var first = await _httpClient.GetAsync(_proxy.TrackCoverUrl(fixture.SeededTrackId));
        var etag = first.Headers.ETag;
        etag.ShouldNotBeNull();

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, _proxy.TrackCoverUrl(fixture.SeededTrackId));
        conditionalRequest.Headers.IfNoneMatch.Add(etag);

        var second = await _httpClient.SendAsync(conditionalRequest);

        second.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task UnmatchedRoute_Returns404()
    {
        var unmatchedRoute = new UriBuilder(_proxy.MusicUrl(fixture.SeededTrackId)) { Path = "/not-a-real-route" }.Uri;

        var response = await _httpClient.GetAsync(unmatchedRoute);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidRouteWithoutSecretSegment_Returns404()
    {
        var withoutSecret = new UriBuilder(_proxy.MusicUrl(fixture.SeededTrackId))
        {
            Path = $"/music/{fixture.SeededTrackId}"
        }.Uri;

        var response = await _httpClient.GetAsync(withoutSecret);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
