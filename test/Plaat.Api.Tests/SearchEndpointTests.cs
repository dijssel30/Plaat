using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class SearchEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_FiltersEachSectionAgainstTheCachedCatalog()
    {
        factory.TrackRepository.GetAll(Arg.Any<CancellationToken>()).Returns(
        [
            Track(1, "Motörhead Live", "Motörhead", "Best Of"),
            Track(2, "Unrelated", "Someone", "Other")
        ]);
        factory.AlbumRepository.GetAlbums(Arg.Any<CancellationToken>()).Returns(
        [
            new AlbumOnlyDto { Id = 10, Title = "Motorhead Anthology", Artists = [new ArtistOnlyDto { Name = "Motörhead" }] },
            new AlbumOnlyDto { Id = 11, Title = "Nope", Artists = [new ArtistOnlyDto { Name = "X" }] }
        ]);
        factory.ArtistRepository.GetArtists(Arg.Any<CancellationToken>()).Returns(
        [
            new ArtistOnlyDto { Id = 20, Name = "Motörhead" },
            new ArtistOnlyDto { Id = 21, Name = "Beatles" }
        ]);

        var response = await _client.GetAsync("/api/search?q=motorhead");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        results.ShouldNotBeNull();
        results.Tracks.ShouldHaveSingleItem().Title.ShouldBe("Motörhead Live");
        results.Albums.ShouldHaveSingleItem().Id.ShouldBe(10u);
        results.Artists.ShouldHaveSingleItem().Id.ShouldBe(20u);
    }

    [Fact]
    public async Task Search_WithBlankQuery_ReturnsEmptySections()
    {
        var response = await _client.GetAsync("/api/search?q=");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        results.ShouldNotBeNull();
        results.Tracks.ShouldBeEmpty();
        results.Albums.ShouldBeEmpty();
        results.Artists.ShouldBeEmpty();
    }

    private static TrackDto Track(uint id, string title, string artist, string album)
    {
        return new()
        {
            Id = id,
            Title = title,
            Artists = [new ArtistOnlyDto { Id = id, Name = artist }],
            Album = album,
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
