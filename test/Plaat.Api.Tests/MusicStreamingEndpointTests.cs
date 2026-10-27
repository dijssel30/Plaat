using System.Net;
using System.Text;
using NSubstitute;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

/// <summary>
/// GET /api/music/{id}: the audio stream every player reads, including its hand-rolled HTTP Range
/// handling (seeking in the web player and the mobile app's MediaElement both depend on it).
/// </summary>
public sealed class MusicStreamingEndpointTests : IClassFixture<PlaatApiWebApplicationFactory>, IDisposable
{
    private const string Content = "0123456789";
    private const uint TrackId = 7001;

    private readonly PlaatApiWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _audioPath = Path.Combine(Path.GetTempPath(), $"plaat-stream-{Guid.NewGuid():N}.mp3");

    public MusicStreamingEndpointTests(PlaatApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        File.WriteAllText(_audioPath, Content, Encoding.ASCII);
        factory.TrackRepository.GetPath(TrackId, Arg.Any<CancellationToken>())
            .Returns(new TrackPath { Id = TrackId, Path = _audioPath, MimeType = "audio/mpeg" });
    }

    public void Dispose()
    {
        File.Delete(_audioPath);
    }

    private async Task<HttpResponseMessage> Get(string? range = null, uint trackId = TrackId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/music/{trackId}");
        if (range is not null)
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task WithoutRange_ReturnsWholeFileAndAdvertisesRangeSupport()
    {
        using var response = await Get();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.AcceptRanges.ShouldContain("bytes");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("audio/mpeg");
        (await response.Content.ReadAsStringAsync()).ShouldBe(Content);
    }

    [Theory]
    [InlineData("bytes=2-5", "2345", 2, 5)]
    [InlineData("bytes=7-", "789", 7, 9)]
    [InlineData("bytes=-3", "789", 7, 9)]
    [InlineData("bytes=-50", Content, 0, 9)]
    [InlineData("bytes=5-100", "56789", 5, 9)]
    [InlineData("BYTES= 0 - 0 ", "0", 0, 0)]
    public async Task SatisfiableRange_ReturnsPartialContent(string range, string expectedBody, long from, long to)
    {
        using var response = await Get(range);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.ToString().ShouldBe($"bytes {from}-{to}/{Content.Length}");
        response.Content.Headers.ContentLength.ShouldBe(expectedBody.Length);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("audio/mpeg");
        (await response.Content.ReadAsStringAsync()).ShouldBe(expectedBody);
    }

    [Theory]
    [InlineData("items=0-1")]
    [InlineData("bytes=0-1,3-4")]
    [InlineData("bytes=1-2-3")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=-x")]
    [InlineData("bytes=x-2")]
    [InlineData("bytes=-1-")]
    [InlineData("bytes=3-x")]
    [InlineData("bytes=5-2")]
    [InlineData("bytes=10-")]
    public async Task UnsatisfiableOrMalformedRange_Returns416WithTotalLength(string range)
    {
        using var response = await Get(range);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestedRangeNotSatisfiable);
        response.Content.Headers.ContentRange!.ToString().ShouldBe($"bytes */{Content.Length}");
    }

    [Fact]
    public async Task UnknownTrack_ReturnsNotFound()
    {
        _factory.TrackRepository.GetPath(7002, Arg.Any<CancellationToken>()).Returns((TrackPath?)null);

        using var response = await Get(trackId: 7002);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FileMissingOnDisk_ReturnsNotFound()
    {
        _factory.TrackRepository.GetPath(7003, Arg.Any<CancellationToken>())
            .Returns(new TrackPath { Id = 7003, Path = "/nonexistent/file.mp3", MimeType = "audio/mpeg" });

        using var response = await Get(trackId: 7003);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
