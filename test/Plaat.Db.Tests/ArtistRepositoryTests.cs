using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class ArtistRepositoryTests
{
    [Fact]
    public async Task TryClaimForEnrichment_NotAttempted_ClaimsAndReturnsTrue()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        var claimed = await repo.TryClaimForEnrichment(artist.Id, CancellationToken.None);

        claimed.ShouldBeTrue();

        using var ctx = fixture.CreateContext();
        var stored = ctx.Artists.Single(a => a.Id == artist.Id);
        stored.DiscogsLookupStatus.ShouldBe(ArtistDiscogsLookupStatus.InProgress);
    }

    [Fact]
    public async Task TryClaimForEnrichment_AlreadyClaimed_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        await repo.TryClaimForEnrichment(artist.Id, CancellationToken.None);
        var secondClaim = await repo.TryClaimForEnrichment(artist.Id, CancellationToken.None);

        secondClaim.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveEnrichmentResult_SavesFieldsAndInvalidatesCache()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        // Prime the cache
        await repo.GetArtist(artist.Id, CancellationToken.None);

        await repo.SaveEnrichmentResult(
            artist.Id, 123, "A short bio", "some-guid.jpg", ArtistDiscogsLookupStatus.Found, CancellationToken.None);

        var updated = await repo.GetArtist(artist.Id, CancellationToken.None);

        updated.ShouldNotBeNull();
        updated.DiscogsSummary.ShouldBe("A short bio");
        updated.HasDiscogsImage.ShouldBeTrue();
        updated.DiscogsLookupStatus.ShouldBe(Contract.Models.ArtistDiscogsLookupStatus.Found);
    }

    [Fact]
    public async Task SaveEnrichmentResult_NotFound_SavesStatusWithoutData()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        await repo.SaveEnrichmentResult(
            artist.Id, null, null, null, ArtistDiscogsLookupStatus.NotFound, CancellationToken.None);

        var updated = await repo.GetArtist(artist.Id, CancellationToken.None);

        updated.ShouldNotBeNull();
        updated.DiscogsLookupStatus.ShouldBe(Contract.Models.ArtistDiscogsLookupStatus.NotFound);
        updated.DiscogsSummary.ShouldBeNull();
        updated.HasDiscogsImage.ShouldBeFalse();
    }

    [Fact]
    public async Task ResetToNotAttempted_RestoresStatus()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        await repo.TryClaimForEnrichment(artist.Id, CancellationToken.None);
        await repo.ResetToNotAttempted(artist.Id, CancellationToken.None);

        var reclaimed = await repo.TryClaimForEnrichment(artist.Id, CancellationToken.None);

        reclaimed.ShouldBeTrue();
    }

    [Fact]
    public async Task GetArtistName_ReturnsName()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        var name = await repo.GetArtistName(artist.Id, CancellationToken.None);

        name.ShouldBe("Test Artist");
    }

    [Fact]
    public async Task GetDiscogsImagePath_WhenSet_ReturnsIt()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var repo = fixture.CreateArtistRepository();

        await repo.SaveEnrichmentResult(
            artist.Id, 1, null, "image.jpg", ArtistDiscogsLookupStatus.Found, CancellationToken.None);

        var path = await repo.GetDiscogsImagePath(artist.Id, CancellationToken.None);

        path.ShouldBe("image.jpg");
    }

    [Fact]
    public async Task IsArtistFavorite_WhenNotFavorited_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var user = fixture.SeedUser("testuser");
        var repo = fixture.CreateArtistRepository();

        var isFavorite = await repo.IsArtistFavorite(artist.Id, user.Id, CancellationToken.None);

        isFavorite.ShouldBeFalse();
    }

    [Fact]
    public async Task SetArtistFavorite_True_MarksItFavorited()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var user = fixture.SeedUser("testuser");
        var repo = fixture.CreateArtistRepository();

        await repo.SetArtistFavorite(artist.Id, user.Id, true, CancellationToken.None);

        var isFavorite = await repo.IsArtistFavorite(artist.Id, user.Id, CancellationToken.None);
        isFavorite.ShouldBeTrue();
    }

    [Fact]
    public async Task SetArtistFavorite_True_WhenAlreadyFavorited_DoesNotThrow()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var user = fixture.SeedUser("testuser");
        var repo = fixture.CreateArtistRepository();

        await repo.SetArtistFavorite(artist.Id, user.Id, true, CancellationToken.None);
        await repo.SetArtistFavorite(artist.Id, user.Id, true, CancellationToken.None);

        var isFavorite = await repo.IsArtistFavorite(artist.Id, user.Id, CancellationToken.None);
        isFavorite.ShouldBeTrue();
    }

    [Fact]
    public async Task SetArtistFavorite_False_RemovesFavorite()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var user = fixture.SeedUser("testuser");
        var repo = fixture.CreateArtistRepository();

        await repo.SetArtistFavorite(artist.Id, user.Id, true, CancellationToken.None);
        await repo.SetArtistFavorite(artist.Id, user.Id, false, CancellationToken.None);

        var isFavorite = await repo.IsArtistFavorite(artist.Id, user.Id, CancellationToken.None);
        isFavorite.ShouldBeFalse();
    }

    [Fact]
    public async Task GetFavoriteArtists_ReturnsOnlyTheUsersFavorites()
    {
        using var fixture = new DbTestFixture();
        var favorited = fixture.SeedArtist("Favorited Artist");
        fixture.SeedArtist("Other Artist");
        var user = fixture.SeedUser("testuser");
        var repo = fixture.CreateArtistRepository();

        await repo.SetArtistFavorite(favorited.Id, user.Id, true, CancellationToken.None);

        var favorites = await repo.GetFavoriteArtists(user.Id, CancellationToken.None);

        favorites.ShouldHaveSingleItem();
        favorites.First().Name.ShouldBe("Favorited Artist");
    }
}
