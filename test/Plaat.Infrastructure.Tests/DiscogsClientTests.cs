using System.Net;
using System.Text;
using Plaat.Infrastructure.Discogs;
using Shouldly;
using Xunit;

namespace Plaat.Infrastructure.Tests;

public sealed class DiscogsClientTests
{
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private static DiscogsClient BuildClient(string jsonResponse)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.discogs.com/") };

        return new DiscogsClient(httpClient, new DiscogsOptions { RequestsPerMinute = 1000 });
    }

    [Fact]
    public async Task SearchArtistByExactName_LocalNameAllCapsDiscogsTitleCased_StillMatches()
    {
        const string json = """{ "results": [ { "id": 42, "title": "Muse", "type": "artist" } ] }""";
        var client = BuildClient(json);

        var result = await client.SearchArtistByExactName("MUSE", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(42);
    }

    [Fact]
    public async Task SearchArtistByExactName_LocalNameCasedDiscogsTitleAllCaps_StillMatches()
    {
        const string json = """{ "results": [ { "id": 42, "title": "MUSE", "type": "artist" } ] }""";
        var client = BuildClient(json);

        var result = await client.SearchArtistByExactName("Muse", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(42);
    }

    [Fact]
    public async Task SearchArtistByExactName_GenuinelyDifferentName_DoesNotMatch()
    {
        const string json = """{ "results": [ { "id": 42, "title": "Muse (band)", "type": "artist" } ] }""";
        var client = BuildClient(json);

        var result = await client.SearchArtistByExactName("Muse", CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SearchArtistByExactName_TitleHasDisambiguationSuffix_StillMatches()
    {
        const string json = """{ "results": [ { "id": 42, "title": "Sam Smith (12)", "type": "artist" } ] }""";
        var client = BuildClient(json);

        var result = await client.SearchArtistByExactName("Sam Smith", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(42);
        result.Title.ShouldBe("Sam Smith");
    }

    [Fact]
    public async Task SearchArtistByExactName_MultipleDisambiguatedMatches_PicksFirstAsMostPopular()
    {
        const string json = """
            {
                "results": [
                    { "id": 100, "title": "Sam Smith (2)", "type": "artist" },
                    { "id": 200, "title": "Sam Smith (12)", "type": "artist" }
                ]
            }
            """;
        var client = BuildClient(json);

        var result = await client.SearchArtistByExactName("Sam Smith", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(100);
        result.Title.ShouldBe("Sam Smith");
    }
}
