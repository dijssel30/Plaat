using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class MusicEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetTrackDetails_WhenTrackExists_ReturnsOkWithDetails()
    {
        factory.TrackRepository.GetTrackDetails(42, Arg.Any<CancellationToken>())
            .Returns(new TrackDetailsDto
            {
                Id = 42,
                Title = "Test Track",
                Artists = [],
                Album = "Test Album",
                AlbumId = 1,
                PlayCount = 3,
                TrackNumber = 1,
                DiscNumber = 1,
                Year = 2024,
                Duration = TimeSpan.FromMinutes(3),
                AddedDate = DateTime.UtcNow,
                PossiblyCorrupt = false,
                BitRate = 320,
                SampleRate = 44100,
                BitsPerSample = 16,
                FileHash = "hash",
                FileSizeBytes = 12345,
                MimeType = "audio/mpeg",
                Locations = [],
                PlayHistory = []
            });

        var response = await _client.GetAsync("/api/music/42/details");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TrackDetailsDto>();
        result.ShouldNotBeNull();
        result.Title.ShouldBe("Test Track");
        result.BitRate.ShouldBe(320);
    }

    [Fact]
    public async Task GetTrackDetails_WhenNotFound_ReturnsNoContent()
    {
        factory.TrackRepository.GetTrackDetails(Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns((TrackDetailsDto?)null);

        var response = await _client.GetAsync("/api/music/999/details");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SetDefaultLocation_CallsRepositoryWithRouteValues()
    {
        factory.TrackRepository.SetDefaultLocation(42, 7, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PutAsync("/api/music/42/locations/7/default", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.TrackRepository.Received(1).SetDefaultLocation(42, 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDefaultLocation_WhenLocationDoesNotBelongToTrack_ReturnsNotFound()
    {
        factory.TrackRepository.SetDefaultLocation(43, 8, Arg.Any<CancellationToken>())
            .Returns(false);

        var response = await _client.PutAsync("/api/music/43/locations/8/default", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetTrackTrim_ValidRange_CallsRepositoryAndReturnsOk()
    {
        factory.TrackRepository.GetDuration(42, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(3));

        var response = await _client.PutAsJsonAsync(
            "/api/music/42/trim", new SetTrackTrimRequest(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(150)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.TrackRepository.Received(1).SetTrimPoints(
            42, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(150), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTrackTrim_ClearingBothPoints_CallsRepositoryWithNulls()
    {
        factory.TrackRepository.GetDuration(46, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(3));

        var response = await _client.PutAsJsonAsync(
            "/api/music/46/trim", new SetTrackTrimRequest(null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.TrackRepository.Received(1).SetTrimPoints(46, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTrackTrim_WhenTrackDoesNotExist_ReturnsNotFound()
    {
        factory.TrackRepository.GetDuration(999, Arg.Any<CancellationToken>())
            .Returns((TimeSpan?)null);

        var response = await _client.PutAsJsonAsync(
            "/api/music/999/trim", new SetTrackTrimRequest(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetTrackTrim_StartAfterEnd_ReturnsBadRequest()
    {
        factory.TrackRepository.GetDuration(44, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(3));

        var response = await _client.PutAsJsonAsync(
            "/api/music/44/trim", new SetTrackTrimRequest(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(10)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await factory.TrackRepository.DidNotReceive().SetTrimPoints(
            44, Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTrackTrim_EndBeyondDuration_ReturnsBadRequest()
    {
        factory.TrackRepository.GetDuration(45, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(3));

        var response = await _client.PutAsJsonAsync(
            "/api/music/45/trim", new SetTrackTrimRequest(null, TimeSpan.FromMinutes(10)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await factory.TrackRepository.DidNotReceive().SetTrimPoints(
            45, Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMusicPage_PassesQueryToRepositoryAndReturnsPagedResult()
    {
        factory.TrackRepository
            .GetTracksPage(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TrackDto> { Items = [MakeTrack(1, "Song A")], TotalCount = 42 });

        var response = await _client.GetAsync("/api/music?search=song&sort=Artist&desc=true&skip=20&take=10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<TrackDto>>();
        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(42);
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Song A");

        await factory.TrackRepository.Received(1).GetTracksPage(
            Arg.Is<TrackQuery>(q =>
                q != null && q.Search == "song" && q.Sort == TrackSortField.Artist && q.Descending && q.Skip == 20 && q.Take == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMusicIds_ReturnsOrderedIdsFromRepository()
    {
        factory.TrackRepository
            .GetTrackIds(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns([3u, 1u, 2u]);

        var response = await _client.GetAsync("/api/music/ids?search=x");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<uint[]>()).ShouldBe([3u, 1u, 2u]);
    }

    [Fact]
    public async Task GetMusicByIds_PostsIdsAndReturnsTracks()
    {
        factory.TrackRepository
            .GetByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns([MakeTrack(5, "Queued")]);

        var response = await _client.PostAsJsonAsync("/api/music/by-ids", new uint[] { 5, 6 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tracks = await response.Content.ReadFromJsonAsync<TrackDto[]>();
        tracks.ShouldNotBeNull();
        tracks.ShouldHaveSingleItem().Title.ShouldBe("Queued");
    }

    [Fact]
    public async Task GetRecentlyPlayed_ReturnsTracksFromRepository()
    {
        factory.TrackRepository
            .GetRecentlyPlayed(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([MakeTrack(9, "Last Played")]);

        var response = await _client.GetAsync("/api/music/recently-played?take=25");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tracks = await response.Content.ReadFromJsonAsync<TrackDto[]>();
        tracks.ShouldNotBeNull();
        tracks.ShouldHaveSingleItem().Title.ShouldBe("Last Played");
        await factory.TrackRepository.Received(1).GetRecentlyPlayed(25, Arg.Any<CancellationToken>());
    }

    private static TrackDto MakeTrack(uint id, string title)
    {
        return new()
        {
            Id = id,
            Title = title,
            Artists = [],
            Album = "Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2024,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    [Fact]
    public async Task GetPossiblyCorrupt_ReturnsTracksFromRepository()
    {
        factory.TrackRepository.GetPossiblyCorrupt(Arg.Any<CancellationToken>())
            .Returns([
                new PossiblyCorruptTrackDto(1, "Bad Track", "Some Artist", "Some Album", 5, ["/music/bad.mp3"])
            ]);

        var response = await _client.GetAsync("/api/music/possibly-corrupt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tracks = await response.Content.ReadFromJsonAsync<PossiblyCorruptTrackDto[]>();
        tracks.ShouldNotBeNull();
        tracks.ShouldHaveSingleItem();
        tracks[0].Title.ShouldBe("Bad Track");
    }
}
