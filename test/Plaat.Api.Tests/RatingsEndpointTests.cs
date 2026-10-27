using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class RatingsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    // TestAuthHandler always authenticates as the user with sub = 1.
    private const int TestUserId = 1;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetTrackRating_ReturnsTheCurrentUsersRating()
    {
        factory.TrackRatingRepository.GetRating(42u, TestUserId, Arg.Any<CancellationToken>())
            .Returns(4);

        var response = await _client.GetAsync("/api/ratings/tracks/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rating = await response.Content.ReadFromJsonAsync<TrackRatingDto>();
        rating.ShouldNotBeNull();
        rating.TrackId.ShouldBe(42u);
        rating.Stars.ShouldBe(4);
    }

    [Fact]
    public async Task GetTrackRating_WhenUnrated_ReturnsZero()
    {
        factory.TrackRatingRepository.GetRating(7u, TestUserId, Arg.Any<CancellationToken>())
            .Returns(TrackRatingDto.MinStars);

        var response = await _client.GetAsync("/api/ratings/tracks/7");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rating = await response.Content.ReadFromJsonAsync<TrackRatingDto>();
        rating.ShouldNotBeNull();
        rating.Stars.ShouldBe(TrackRatingDto.MinStars);
    }

    [Fact]
    public async Task SetTrackRating_PersistsItForTheCurrentUser()
    {
        factory.TrackRatingRepository.SetRating(42u, TestUserId, 5, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PutAsJsonAsync(
            "/api/ratings/tracks/42", new SetTrackRatingRequest(5));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rating = await response.Content.ReadFromJsonAsync<TrackRatingDto>();
        rating.ShouldNotBeNull();
        rating.Stars.ShouldBe(5);

        await factory.TrackRatingRepository.Received()
            .SetRating(42u, TestUserId, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTrackRating_ToZero_IsAccepted()
    {
        factory.TrackRatingRepository.SetRating(9u, TestUserId, 0, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PutAsJsonAsync(
            "/api/ratings/tracks/9", new SetTrackRatingRequest(TrackRatingDto.MinStars));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetTrackRating_UnknownTrack_ReturnsNotFound()
    {
        factory.TrackRatingRepository.SetRating(404u, TestUserId, 3, Arg.Any<CancellationToken>())
            .Returns(false);

        var response = await _client.PutAsJsonAsync(
            "/api/ratings/tracks/404", new SetTrackRatingRequest(3));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task SetTrackRating_OutOfRange_ReturnsBadRequestAndStoresNothing(int stars)
    {
        var response = await _client.PutAsJsonAsync(
            "/api/ratings/tracks/11", new SetTrackRatingRequest(stars));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await factory.TrackRatingRepository.DidNotReceive()
            .SetRating(11u, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
