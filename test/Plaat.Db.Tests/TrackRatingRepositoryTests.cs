using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class TrackRatingRepositoryTests
{
    [Fact]
    public async Task GetRating_WhenNeverRated_ReturnsZero()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");

        var repo = fixture.CreateTrackRatingRepository();

        var rating = await repo.GetRating(track.Id, alice.Id, CancellationToken.None);

        rating.ShouldBe(TrackRating.MinStars);
    }

    [Fact]
    public async Task SetRating_StoresAndReadsBack()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");

        var repo = fixture.CreateTrackRatingRepository();

        (await repo.SetRating(track.Id, alice.Id, 4, CancellationToken.None)).ShouldBeTrue();

        (await repo.GetRating(track.Id, alice.Id, CancellationToken.None)).ShouldBe(4);
    }

    [Fact]
    public async Task SetRating_WhenAlreadyRated_Overwrites()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");
        fixture.SeedRating(track, alice, 2);

        var repo = fixture.CreateTrackRatingRepository();

        await repo.SetRating(track.Id, alice.Id, 5, CancellationToken.None);

        (await repo.GetRating(track.Id, alice.Id, CancellationToken.None)).ShouldBe(5);

        await using var context = fixture.CreateContext();
        context.Set<TrackRating>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task SetRating_ToZero_DeletesTheRowRatherThanStoringAZero()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");
        fixture.SeedRating(track, alice, 3);

        var repo = fixture.CreateTrackRatingRepository();

        await repo.SetRating(track.Id, alice.Id, TrackRating.MinStars, CancellationToken.None);

        (await repo.GetRating(track.Id, alice.Id, CancellationToken.None)).ShouldBe(TrackRating.MinStars);

        await using var context = fixture.CreateContext();
        context.Set<TrackRating>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(9, TrackRating.MaxStars)]
    [InlineData(-3, TrackRating.MinStars)]
    public async Task SetRating_OutOfRange_IsClamped(int requested, int expected)
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");

        var repo = fixture.CreateTrackRatingRepository();

        await repo.SetRating(track.Id, alice.Id, requested, CancellationToken.None);

        (await repo.GetRating(track.Id, alice.Id, CancellationToken.None)).ShouldBe(expected);
    }

    [Fact]
    public async Task SetRating_UnknownTrack_ReturnsFalseAndStoresNothing()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreateTrackRatingRepository();

        (await repo.SetRating(404u, alice.Id, 3, CancellationToken.None)).ShouldBeFalse();

        await using var context = fixture.CreateContext();
        context.Set<TrackRating>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Ratings_AreSeparatePerUser()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "Track");

        var repo = fixture.CreateTrackRatingRepository();

        await repo.SetRating(track.Id, alice.Id, 5, CancellationToken.None);
        await repo.SetRating(track.Id, bob.Id, 1, CancellationToken.None);

        (await repo.GetRating(track.Id, alice.Id, CancellationToken.None)).ShouldBe(5);
        (await repo.GetRating(track.Id, bob.Id, CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task GetRatings_ReturnsOnlyTheUsersOwnRatedTracks()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Album");
        var rated = fixture.SeedTrack(album, "Rated", "/music/rated.mp3");
        var unrated = fixture.SeedTrack(album, "Unrated", "/music/unrated.mp3");
        var ratedByBob = fixture.SeedTrack(album, "Bob's", "/music/bobs.mp3");

        fixture.SeedRating(rated, alice, 3);
        fixture.SeedRating(ratedByBob, bob, 4);

        var repo = fixture.CreateTrackRatingRepository();

        var ratings = await repo.GetRatings(
            [rated.Id, unrated.Id, ratedByBob.Id], alice.Id, CancellationToken.None);

        ratings.Count.ShouldBe(1);
        ratings[rated.Id].ShouldBe(3);
    }

    [Fact]
    public async Task GetRatings_WithNoTrackIds_ReturnsEmpty()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreateTrackRatingRepository();

        (await repo.GetRatings([], alice.Id, CancellationToken.None)).ShouldBeEmpty();
    }
}
