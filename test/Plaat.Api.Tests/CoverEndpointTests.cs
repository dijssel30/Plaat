using System.Net;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class CoverEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetCoverArtByTrackId_WithoutTrailingSlash_MatchesRoute()
    {
        var response = await _client.GetAsync("/api/cover/track/42");

        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCoverArtByAlbumId_WithoutTrailingSlash_MatchesRoute()
    {
        var response = await _client.GetAsync("/api/cover/album/42");

        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCoverArt_SetsCacheControlAndEtag()
    {
        var response = await _client.GetAsync("/api/cover/album/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromHours(1));
        response.Headers.CacheControl.Private.ShouldBeTrue();
        response.Headers.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetCoverArt_WithMatchingIfNoneMatch_Returns304()
    {
        var first = await _client.GetAsync("/api/cover/album/42");
        var etag = first.Headers.ETag!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/cover/album/42");
        request.Headers.IfNoneMatch.Add(etag);

        var second = await _client.SendAsync(request);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }
}
