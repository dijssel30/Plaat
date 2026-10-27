using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models;
using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

/// <summary>
/// Track sorting/ID queries, list-cache refreshes, recently-added grouping, favorites, cover art,
/// password/refresh-token edge cases and library-growth edge cases.
/// </summary>
public sealed class RepositoryGapTests : IDisposable
{
    private readonly DbTestFixture _fixture = new();

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private (Track low, Track high) SeedSortableTracks()
    {
        var albumA = _fixture.SeedAlbum("A Album");
        var albumB = _fixture.SeedAlbum("B Album");
        var low = _fixture.SeedTrack(albumA, "Alpha", "/low.mp3", playCount: 1, year: 1990, artistName: "Aardvark");
        var high = _fixture.SeedTrack(albumB, "Beta", "/high.mp3", playCount: 9, year: 2020, artistName: "Zebra");

        return (low, high);
    }

    [Theory]
    [InlineData(TrackSortField.Title)]
    [InlineData(TrackSortField.Artist)]
    [InlineData(TrackSortField.Album)]
    [InlineData(TrackSortField.Year)]
    [InlineData(TrackSortField.Plays)]
    public async Task GetTracksPage_EachSortField_OrdersAscendingAndDescending(TrackSortField sort)
    {
        var (low, high) = SeedSortableTracks();
        var repo = _fixture.CreateTrackRepository();

        var ascending = await repo.GetTracksPage(new TrackQuery(Sort: sort), CancellationToken.None);
        var descending = await repo.GetTracksPage(new TrackQuery(Sort: sort, Descending: true), CancellationToken.None);

        ascending.Items.Select(t => t.Id).ShouldBe([low.Id, high.Id]);
        descending.Items.Select(t => t.Id).ShouldBe([high.Id, low.Id]);
    }

    [Fact]
    public async Task GetTracksPage_UnknownSortField_KeepsLibraryOrder()
    {
        var (low, high) = SeedSortableTracks();

        var page = await _fixture.CreateTrackRepository()
            .GetTracksPage(new TrackQuery(Sort: (TrackSortField)99), CancellationToken.None);

        page.Items.Select(t => t.Id).Order().ShouldBe(new[] { low.Id, high.Id }.Order());
    }

    [Fact]
    public async Task GetTrackIds_ReturnsEveryMatchInSortOrder()
    {
        var (low, high) = SeedSortableTracks();
        var repo = _fixture.CreateTrackRepository();

        (await repo.GetTrackIds(new TrackQuery(Sort: TrackSortField.Plays, Descending: true, Take: 1), CancellationToken.None))
            .ShouldBe([high.Id, low.Id]);
        (await repo.GetTrackIds(new TrackQuery(Search: "zebra"), CancellationToken.None))
            .ShouldBe([high.Id]);
    }

    [Fact]
    public async Task RefreshListCaches_PickUpRowsAddedOutsideTheRepositories()
    {
        var tracks = _fixture.CreateTrackRepository();
        var albums = _fixture.CreateAlbumRepository();
        var artists = _fixture.CreateArtistRepository();
        (await tracks.GetAll(CancellationToken.None)).ShouldBeEmpty();
        (await albums.GetAlbums(CancellationToken.None)).ShouldBeEmpty();
        (await artists.GetArtists(CancellationToken.None)).ShouldBeEmpty();

        _fixture.SeedTrack(_fixture.SeedAlbum("New Album"), "New", "/new.mp3", artistName: "New Artist");
        (await tracks.GetAll(CancellationToken.None)).ShouldBeEmpty();

        await tracks.RefreshListCache(CancellationToken.None);
        await albums.RefreshListCache(CancellationToken.None);
        await artists.RefreshListCache(CancellationToken.None);

        (await tracks.GetAll(CancellationToken.None)).ShouldHaveSingleItem().Title.ShouldBe("New");
        (await albums.GetAlbums(CancellationToken.None)).ShouldHaveSingleItem().Title.ShouldBe("New Album");
        (await artists.GetArtists(CancellationToken.None)).ShouldHaveSingleItem().Name.ShouldBe("New Artist");
    }

    [Fact]
    public async Task GetArtist_Unknown_ReturnsNull()
    {
        (await _fixture.CreateArtistRepository().GetArtist(404, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GetRecentlyAdded_GroupsMultiTrackAlbumsAndListsSinglesSeparately()
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var fullAlbum = _fixture.SeedAlbum("Full Album");
        _fixture.SeedTrack(fullAlbum, "Side A", "/full-1.mp3");
        _fixture.SeedTrack(fullAlbum, "Side B", "/full-2.mp3");
        var single = _fixture.SeedTrack(_fixture.SeedAlbum("Single"), "Single", "/single.mp3");
        _fixture.SeedTrack(_fixture.SeedAlbum("Old"), "Old", "/old.mp3", addedDate: DateTime.UtcNow.AddYears(-1));

        var recent = await _fixture.CreateTrackRepository().GetRecentlyAdded(since, CancellationToken.None);

        recent.Albums.ShouldHaveSingleItem().Title.ShouldBe("Full Album");
        recent.Tracks.ShouldHaveSingleItem().Id.ShouldBe(single.Id);
    }

    [Fact]
    public async Task TrackGetCoverArt_UsesItsAlbumsCoverOrNull()
    {
        var album = _fixture.SeedAlbum("Covered");
        var covered = _fixture.SeedTrack(album, "Covered", "/covered.mp3");
        var bare = _fixture.SeedTrack(_fixture.SeedAlbum("Bare"), "Bare", "/bare.mp3");
        await _fixture.CreateAlbumRepository().SetCoverArt(album.Id, "c.webp", "HASH", "image/webp", CancellationToken.None);
        var repo = _fixture.CreateTrackRepository();

        var cover = await repo.GetCoverArt(covered.Id, CancellationToken.None);

        cover.ShouldNotBeNull();
        cover.Hash.ShouldBe("HASH");
        cover.Path.ShouldBe("c.webp");
        (await repo.GetCoverArt(bare.Id, CancellationToken.None)).ShouldBeNull();
        (await repo.GetCoverArt(404, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SetTrimPoints_UpdatesEveryCachedViewOfTheTrack()
    {
        var album = _fixture.SeedAlbum("Album");
        var track = _fixture.SeedTrack(album, "Trimmed", "/t.mp3", duration: TimeSpan.FromMinutes(5), artistName: "Artist");
        var tracks = _fixture.CreateTrackRepository();
        var all = await tracks.GetAll(CancellationToken.None);
        var albumDetails = await _fixture.CreateAlbumRepository().GetAlbum(album.Id, CancellationToken.None);
        var artistId = all.Single().Artists.Single().Id;
        var artistDetails = await _fixture.CreateArtistRepository().GetArtist(artistId, CancellationToken.None);
        var recent = await tracks.GetRecentlyAdded(DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        await tracks.SetTrimPoints(track.Id, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), CancellationToken.None);

        foreach (var dto in new[] { all.Single(), albumDetails!.Tracks.Single(), artistDetails!.Tracks.Single(), recent.Tracks.Single() })
        {
            dto.TrimStart.ShouldBe(TimeSpan.FromSeconds(1));
            dto.TrimEnd.ShouldBe(TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public async Task AlbumFavorites_AddIsIdempotentAndRemoveWorksPerUser()
    {
        var alice = _fixture.SeedUser("alice");
        var bob = _fixture.SeedUser("bob");
        var album = _fixture.SeedAlbum("Loved");
        var repo = _fixture.CreateAlbumRepository();

        await repo.SetAlbumFavorite(album.Id, alice.Id, true, CancellationToken.None);
        await repo.SetAlbumFavorite(album.Id, alice.Id, true, CancellationToken.None);

        (await repo.IsAlbumFavorite(album.Id, alice.Id, CancellationToken.None)).ShouldBeTrue();
        (await repo.IsAlbumFavorite(album.Id, bob.Id, CancellationToken.None)).ShouldBeFalse();
        (await repo.GetFavoriteAlbums(alice.Id, CancellationToken.None)).ShouldHaveSingleItem().Title.ShouldBe("Loved");
        (await repo.GetFavoriteAlbums(bob.Id, CancellationToken.None)).ShouldBeEmpty();

        await repo.SetAlbumFavorite(album.Id, alice.Id, false, CancellationToken.None);

        (await repo.IsAlbumFavorite(album.Id, alice.Id, CancellationToken.None)).ShouldBeFalse();
        (await repo.GetFavoriteAlbums(alice.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateHashedPassword_ChangesExistingUserAndIgnoresUnknown()
    {
        _fixture.SeedUser("alice", passwordHash: "old");
        var repo = _fixture.CreateUserRepository();

        await repo.UpdateHashedPassword("alice", "new", CancellationToken.None);
        await repo.UpdateHashedPassword("nobody", "whatever", CancellationToken.None);

        (await repo.GetPasswordHash("alice", CancellationToken.None)).ShouldBe("new");
        (await repo.GetPasswordHash("nobody", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task CreateRefreshToken_UnknownUser_Throws()
    {
        await Should.ThrowAsync<Exception>(() => _fixture.CreateUserRepository().CreateRefreshToken("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task RotateRefreshToken_Expired_ReturnsNull()
    {
        _fixture.SeedUser("alice");
        var repo = _fixture.CreateUserRepository();
        var issued = await repo.CreateRefreshToken("alice", CancellationToken.None);
        await using (var context = _fixture.CreateContext())
        {
            await context.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        (await repo.RotateRefreshToken(issued.RefreshToken, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GetStats_TracksAddedOnlyInTheFuture_EndsGrowthCurveAtThem()
    {
        var future = DateTime.UtcNow.AddMonths(3);
        _fixture.SeedTrack(_fixture.SeedAlbum("Future"), "Future", "/future.mp3", addedDate: future);

        var stats = await _fixture.CreateStatsRepository().GetStats(StatsRange.All, null, CancellationToken.None);

        var last = stats.Library.Growth.Last();
        last.Month.Year.ShouldBe(future.Year);
        last.Month.Month.ShouldBe(future.Month);
    }
}
