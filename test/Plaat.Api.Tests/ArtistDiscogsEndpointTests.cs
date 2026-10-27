using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class ArtistDiscogsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetDiscogsImage_WhenArtistHasNoImage_ReturnsNoContent()
    {
        factory.ArtistRepository.GetDiscogsImagePath(7, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var response = await _client.GetAsync("/api/artists/7/discogs-image");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetArtist_NeverTriesToClaimForEnrichment()
    {
        factory.ArtistRepository.GetArtist(10, Arg.Any<CancellationToken>())
            .Returns(new ArtistDetailsDto
            {
                Id = 10,
                Name = "Test Artist",
                Tracks = [],
                Albums = [],
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted
            });

        var response = await _client.GetAsync("/api/artists/10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Enrichment is now triggered by a separate POST {id}/enrich call - GetArtist must stay a
        // pure read with no side effects. Scoped to this test's own artist ID since ArtistRepository
        // is a single substitute shared across every test in this class.
        await factory.ArtistRepository.DidNotReceive()
            .TryClaimForEnrichment(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enrich_WhenLookupNotAttempted_TriesToClaimAndReturnsRefreshedArtist()
    {
        factory.ArtistRepository.GetArtist(11, Arg.Any<CancellationToken>())
            .Returns(new ArtistDetailsDto
            {
                Id = 11,
                Name = "Test Artist",
                Tracks = [],
                Albums = [],
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.NotAttempted
            });

        var response = await _client.PostAsync("/api/artists/11/enrich", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Test config has the Discogs feature enabled (see PlaatApiWebApplicationFactory), so a
        // NotAttempted artist should trigger a claim attempt - the enable/disable gate itself is
        // covered independently in Plaat.Biz.Tests.ArtistEnrichmentQueueServiceTests.
        await factory.ArtistRepository.Received(1)
            .TryClaimForEnrichment(11, Arg.Any<CancellationToken>());

        var artist = await response.Content.ReadFromJsonAsync<ArtistDetailsDto>();
        artist.ShouldNotBeNull();
        artist.Id.ShouldBe(11u);
    }

    [Fact]
    public async Task Enrich_WhenLookupAlreadyFound_DoesNotTryToClaim()
    {
        factory.ArtistRepository.GetArtist(12, Arg.Any<CancellationToken>())
            .Returns(new ArtistDetailsDto
            {
                Id = 12,
                Name = "Test Artist",
                Tracks = [],
                Albums = [],
                DiscogsSummary = "Already loaded",
                DiscogsLookupStatus = ArtistDiscogsLookupStatus.Found
            });

        var response = await _client.PostAsync("/api/artists/12/enrich", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.ArtistRepository.DidNotReceive()
            .TryClaimForEnrichment(12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enrich_WhenArtistDoesNotExist_ReturnsNoContent()
    {
        factory.ArtistRepository.GetArtist(999, Arg.Any<CancellationToken>())
            .Returns((ArtistDetailsDto?)null);

        var response = await _client.PostAsync("/api/artists/999/enrich", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
