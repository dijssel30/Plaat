using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models;
using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class TrackRepositoryPlayCountTests
{
    [Fact]
    public async Task IncreasePlayCount_BumpsCachedListsInPlace_WithoutRebuilding()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "One", "/music/one.mp3");
        LinkGenreAndArtist(fixture, track.Id, "Rock", "The Band");

        var repo = fixture.CreateTrackRepository();

        var listBefore = await repo.GetAll(CancellationToken.None);
        var albumBefore = await fixture.CreateAlbumRepository().GetAlbum(album.Id, CancellationToken.None);
        listBefore.Single().PlayCount.ShouldBe(0u);

        await repo.IncreasePlayCount(track.Id, null, CancellationToken.None);

        var listAfter = await repo.GetAll(CancellationToken.None);

        // Same cached collection instance - it was edited in place, not evicted and rebuilt.
        listAfter.ShouldBeSameAs(listBefore);
        listAfter.Single().PlayCount.ShouldBe(1u);
        albumBefore!.Tracks.Single().PlayCount.ShouldBe(1u);
    }

    [Fact]
    public async Task IncreasePlayCount_ShowsUpInStats_EvenThoughTheyWereAlreadyRead()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "One", "/music/one.mp3");
        LinkGenreAndArtist(fixture, track.Id, "Rock", "The Band");

        var repo = fixture.CreateTrackRepository();
        var statsRepo = fixture.CreateStatsRepository();

        var before = await statsRepo.GetStats(StatsRange.All, null, CancellationToken.None);
        var historyBefore = before.PlayHistory.Count;

        await repo.IncreasePlayCount(track.Id, null, CancellationToken.None);

        var after = await statsRepo.GetStats(StatsRange.All, null, CancellationToken.None);

        after.PlayHistory.Count.ShouldBe(historyBefore + 1);
        after.TopGenres.ShouldContain(g => g.Name == "Rock" && g.Count == 1);
        after.TopArtists.ShouldContain(a => a.Name == "The Band" && a.Count == 1);
    }

    private static void LinkGenreAndArtist(DbTestFixture fixture, uint trackId, string genre, string artist)
    {
        using var ctx = fixture.CreateContext();
        var track = ctx.Tracks
            .Include(t => t.Genres)
            .Include(t => t.Artists)
            .Single(t => t.Id == trackId);

        track.Genres.Add(new Genre { Name = genre });
        track.Artists.Add(new Artist { Name = artist, NormalizedName = artist.ToLowerInvariant() });
        ctx.SaveChanges();
    }
}
