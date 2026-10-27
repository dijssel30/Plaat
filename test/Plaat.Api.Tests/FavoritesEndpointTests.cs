using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class FavoritesEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    // TestAuthHandler always authenticates as the user with sub = 1.
    private const int TestUserId = 1;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetFavoriteAlbums_ReturnsTheCurrentUsersFavorites()
    {
        factory.AlbumRepository.GetFavoriteAlbums(TestUserId, Arg.Any<CancellationToken>())
            .Returns([
                new AlbumOnlyDto { Id = 1, Title = "Favorite Album", Artists = [new ArtistOnlyDto { Name = "Test Artist" }] }
            ]);

        var response = await _client.GetAsync("/api/favorites/albums");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var albums = await response.Content.ReadFromJsonAsync<AlbumOnlyDto[]>();
        albums.ShouldNotBeNull();
        albums.ShouldHaveSingleItem();
        albums[0].Title.ShouldBe("Favorite Album");
    }

    [Fact]
    public async Task GetFavoriteArtists_ReturnsTheCurrentUsersFavorites()
    {
        factory.ArtistRepository.GetFavoriteArtists(TestUserId, Arg.Any<CancellationToken>())
            .Returns([
                new ArtistOnlyDto { Id = 1, Name = "Favorite Artist" }
            ]);

        var response = await _client.GetAsync("/api/favorites/artists");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var artists = await response.Content.ReadFromJsonAsync<ArtistOnlyDto[]>();
        artists.ShouldNotBeNull();
        artists.ShouldHaveSingleItem();
        artists[0].Name.ShouldBe("Favorite Artist");
    }
}
