using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Plaat.Infrastructure.Discogs;
using Shouldly;
using Xunit;

namespace Plaat.Infrastructure.Tests;

/// <summary>
/// Request shapes, 404/429 handling, artist image selection, reference resolution and the DI
/// registration's headers.
/// </summary>
public sealed class DiscogsClientHttpTests
{
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public QueueHandler Respond(HttpStatusCode status, string? json = null, TimeSpan? retryAfter = null)
        {
            _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status);
                if (json is not null)
                {
                    response.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                if (retryAfter is { } delay)
                {
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
                }

                return response;
            });

            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private static DiscogsClient Client(QueueHandler handler)
    {
        return new DiscogsClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.discogs.com/") },
            new DiscogsOptions { RequestsPerMinute = 1000 });
    }

    [Fact]
    public async Task SearchArtistByExactName_EscapesQueryAndSkipsNonArtistOrUntitledResults()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, """
            { "results": [
                { "id": 1, "title": "AC/DC", "type": "label" },
                { "id": 2, "title": null, "type": "artist" },
                { "id": 3, "title": " AC/DC (2) ", "type": "Artist" }
            ] }
            """);

        var result = await Client(handler).SearchArtistByExactName(" AC/DC ", CancellationToken.None);

        result.ShouldBe(new DiscogsSearchArtist(3, "AC/DC"));
        handler.Requests.Single().RequestUri!.PathAndQuery.ShouldBe("/database/search?q=%20AC%2FDC%20&type=artist");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task SearchArtistByExactName_NoResults_ReturnsNull(string json)
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, json);

        (await Client(handler).SearchArtistByExactName("x", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SearchArtistByExactName_NotFound_ReturnsNull()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.NotFound);

        (await Client(handler).SearchArtistByExactName("x", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GetArtistById_PrefersThePrimaryImage()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, """
            { "id": 7, "name": "Band", "profile": "Bio",
              "images": [ { "type": "secondary", "uri": "https://img/2.jpg" }, { "type": "PRIMARY", "uri": "https://img/1.jpg" } ] }
            """);

        var details = await Client(handler).GetArtistById(7, CancellationToken.None);

        details.ShouldBe(new DiscogsArtistDetails(7, "Band", "Bio", "https://img/1.jpg"));
        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/artists/7");
    }

    [Fact]
    public async Task GetArtistById_WithoutPrimaryImage_UsesTheFirst()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, """
            { "id": 7, "name": "Band", "images": [ { "type": "secondary", "uri": "https://img/2.jpg" } ] }
            """);

        (await Client(handler).GetArtistById(7, CancellationToken.None))!.ImageUri.ShouldBe("https://img/2.jpg");
    }

    [Fact]
    public async Task GetArtistById_WithoutImages_HasNoImage()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, """{ "id": 7, "name": "Band" }""");

        var details = await Client(handler).GetArtistById(7, CancellationToken.None);

        details!.ImageUri.ShouldBeNull();
        details.Profile.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "id": 7 }""")]
    [InlineData("null")]
    public async Task GetArtistById_WithoutName_ReturnsNull(string json)
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, json);

        (await Client(handler).GetArtistById(7, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GetArtistById_NotFound_ReturnsNull()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.NotFound);

        (await Client(handler).GetArtistById(7, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData('a', "/artists/5", """{ "name": "An Artist" }""", "An Artist")]
    [InlineData('l', "/labels/5", """{ "name": "A Label" }""", "A Label")]
    [InlineData('m', "/masters/5", """{ "title": "A Master" }""", "A Master")]
    [InlineData('r', "/releases/5", """{ "title": "A Release" }""", "A Release")]
    [InlineData('r', "/releases/5", """{ "name": "wrong field" }""", null)]
    public async Task ResolveReferenceName_ReadsTheRightEndpointAndField(char type, string path, string json, string? expected)
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.OK, json);

        var name = await Client(handler).ResolveReferenceName(type, 5, CancellationToken.None);

        name.ShouldBe(expected);
        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe(path);
    }

    [Fact]
    public async Task ResolveReferenceName_UnknownType_ReturnsNullWithoutARequest()
    {
        var handler = new QueueHandler();

        (await Client(handler).ResolveReferenceName('x', 5, CancellationToken.None)).ShouldBeNull();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveReferenceName_NotFound_ReturnsNull()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.NotFound);

        (await Client(handler).ResolveReferenceName('a', 5, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task RateLimited_RetriesOnceAfterTheRequestedDelay()
    {
        var handler = new QueueHandler()
            .Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(10))
            .Respond(HttpStatusCode.OK, """{ "id": 7, "name": "Band" }""");

        (await Client(handler).GetArtistById(7, CancellationToken.None))!.Name.ShouldBe("Band");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RateLimitedTwice_Throws()
    {
        var handler = new QueueHandler()
            .Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(10))
            .Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(10));

        await Should.ThrowAsync<HttpRequestException>(() => Client(handler).GetArtistById(7, CancellationToken.None));
    }

    [Fact]
    public async Task RateLimitedWithoutRetryAfter_WaitsTheDefaultMinute()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.TooManyRequests);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => Client(handler).GetArtistById(7, cts.Token));
        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ServerError_Throws()
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<HttpRequestException>(() => Client(handler).SearchArtistByExactName("x", CancellationToken.None));
    }

    [Theory]
    [InlineData("secret", "Discogs token=secret")]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    public async Task AddDiscogsClient_ConfiguresBaseAddressUserAgentAndOptionalToken(string? token, string? expectedAuthorization)
    {
        var handler = new QueueHandler().Respond(HttpStatusCode.NotFound);
        var services = new ServiceCollection();
        services.AddSingleton(new DiscogsOptions { Token = token, RequestsPerMinute = 1000 });
        services.AddDiscogsClient();
        services.AddHttpClient(nameof(IDiscogsClient)).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IDiscogsClient>().GetArtistById(1, CancellationToken.None);

        var request = handler.Requests.Single();
        request.RequestUri.ShouldBe(new Uri("https://api.discogs.com/artists/1"));
        request.Headers.UserAgent.ToString().ShouldBe("Plaat/1.0");
        (request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null).ShouldBe(expectedAuthorization);
    }
}
