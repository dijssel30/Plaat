using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class ArtistsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    // TestAuthHandler always authenticates as the user with sub = 1.
    private const int TestUserId = 1;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAllArtists_WhenEmpty_ReturnsOkWithEmptyList()
    {
        factory.ArtistRepository.GetArtists(Arg.Any<CancellationToken>())
            .Returns([]);

        var response = await _client.GetAsync("/api/artists");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var artists = await response.Content.ReadFromJsonAsync<ArtistOnlyDto[]>();
        artists.ShouldNotBeNull();
        artists.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllArtists_WhenArtistsExist_ReturnsAll()
    {
        factory.ArtistRepository.GetArtists(Arg.Any<CancellationToken>())
            .Returns([
                new ArtistOnlyDto { Id = 1, Name = "Artist One" },
                new ArtistOnlyDto { Id = 2, Name = "Artist Two" }
            ]);

        var response = await _client.GetAsync("/api/artists");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var artists = await response.Content.ReadFromJsonAsync<ArtistOnlyDto[]>();
        artists.ShouldNotBeNull();
        artists.Length.ShouldBe(2);
    }

    [Fact]
    public async Task GetArtist_WhenArtistExists_ReturnsOkWithArtist()
    {
        factory.ArtistRepository.GetArtist(7, Arg.Any<CancellationToken>())
            .Returns(new ArtistDetailsDto
            {
                Id = 7,
                Name = "Test Artist",
                Tracks = [],
                Albums = [],
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted
            });

        var response = await _client.GetAsync("/api/artists/7");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ArtistDetailsDto>();
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Test Artist");
        result.Id.ShouldBe(7u);
    }

    [Fact]
    public async Task GetArtist_WhenNotFound_ReturnsNoContent()
    {
        factory.ArtistRepository.GetArtist(Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns((ArtistDetailsDto?)null);

        var response = await _client.GetAsync("/api/artists/999");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetArtistFavorite_ReturnsWhetherCurrentUserFavoritedIt()
    {
        factory.ArtistRepository.IsArtistFavorite(7u, TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.GetAsync("/api/artists/7/favorite");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var isFavorite = await response.Content.ReadFromJsonAsync<bool>();
        isFavorite.ShouldBeTrue();
    }

    [Fact]
    public async Task AddArtistFavorite_MarksItAsFavoriteForTheCurrentUser()
    {
        var response = await _client.PutAsync("/api/artists/7/favorite", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.ArtistRepository.Received()
            .SetArtistFavorite(7u, TestUserId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveArtistFavorite_RemovesItForTheCurrentUser()
    {
        var response = await _client.DeleteAsync("/api/artists/7/favorite");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.ArtistRepository.Received()
            .SetArtistFavorite(7u, TestUserId, false, Arg.Any<CancellationToken>());
    }
}
