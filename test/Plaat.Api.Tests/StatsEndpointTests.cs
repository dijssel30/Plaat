using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class StatsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetStats_ReturnsOkWithStats()
    {
        factory.StatsRepository.GetStats(Arg.Any<StatsRange>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new StatsDto
            {
                PlayHistory = [DateTime.UtcNow],
                TopGenres = [new NamedCountDto { Name = "Rock", Count = 5 }],
                TopArtists = [new NamedCountDto { Name = "Test Artist", Count = 3 }],
                Library = new LibraryStatsDto
                {
                    TrackCount = 10,
                    AlbumCount = 2,
                    ArtistCount = 1,
                    PossiblyCorruptCount = 0,
                    TotalDuration = TimeSpan.FromMinutes(42),
                    FormatBreakdown = [new NamedCountDto { Name = "FLAC", Count = 10 }],
                    Growth =
                    [
                        new LibraryGrowthPointDto
                        {
                            Month = new DateTime(2026, 1, 1),
                            Added = 10,
                            TotalTracks = 10
                        }
                    ]
                }
            });

        var response = await _client.GetAsync("/api/stats");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<StatsDto>();
        result.ShouldNotBeNull();
        result.PlayHistory.ShouldHaveSingleItem();
        result.TopGenres.Single().Name.ShouldBe("Rock");
        result.TopArtists.Single().Name.ShouldBe("Test Artist");
        result.Library.TrackCount.ShouldBe(10);
        result.Library.FormatBreakdown.Single().Name.ShouldBe("FLAC");
        result.Library.Growth.Single().TotalTracks.ShouldBe(10);
    }

    [Theory]
    [InlineData("?range=Week&scope=Everyone", StatsRange.Week)]
    [InlineData("?range=Month&scope=Everyone", StatsRange.Month)]
    [InlineData("", StatsRange.All)]
    public async Task GetStats_PassesTheRequestedRangeThrough(string query, StatsRange expected)
    {
        factory.StatsRepository.ClearReceivedCalls();

        await _client.GetAsync($"/api/stats{query}");

        await factory.StatsRepository.Received(1)
            .GetStats(expected, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStats_ScopedToMe_PassesTheCallersUserId()
    {
        factory.StatsRepository.ClearReceivedCalls();

        await _client.GetAsync("/api/stats?range=All&scope=Me");

        await factory.StatsRepository.Received(1)
            .GetStats(StatsRange.All, 1, Arg.Any<CancellationToken>());
    }
}
