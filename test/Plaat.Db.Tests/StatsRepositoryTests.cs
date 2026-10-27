using Plaat.Contract.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class StatsRepositoryTests
{
    [Fact]
    public async Task GetStats_WhenEmpty_ReturnsZeroedStats()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateStatsRepository();

        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.PlayHistory.ShouldBeEmpty();
        result.TopGenres.ShouldBeEmpty();
        result.TopArtists.ShouldBeEmpty();
        result.Library.TrackCount.ShouldBe(0);
        result.Library.AlbumCount.ShouldBe(0);
        result.Library.ArtistCount.ShouldBe(0);
        result.Library.PossiblyCorruptCount.ShouldBe(0);
        result.Library.TotalDuration.ShouldBe(TimeSpan.Zero);
        result.Library.FormatBreakdown.ShouldBeEmpty();
        result.Library.Growth.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStats_IncludesFullPlayHistory()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var trackOne = fixture.SeedTrack(album, "Track One", "/music/one.mp3");
        var trackTwo = fixture.SeedTrack(album, "Track Two", "/music/two.mp3");
        fixture.SeedPlay(trackOne, new DateTime(2026, 1, 1, 12, 0, 0));
        fixture.SeedPlay(trackTwo, new DateTime(2026, 1, 2, 12, 0, 0));

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.PlayHistory.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetStats_TopGenres_CountsPlaysAcrossMultipleTaggedGenres()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        var rock = fixture.SeedGenre("Rock");
        var pop = fixture.SeedGenre("Pop");

        using (var ctx = fixture.CreateContext())
        {
            var trackEntity = ctx.Tracks.Single(t => t.Id == track.Id);
            trackEntity.Genres.Add(ctx.Genres.Single(g => g.Id == rock.Id));
            trackEntity.Genres.Add(ctx.Genres.Single(g => g.Id == pop.Id));
            ctx.SaveChanges();
        }

        fixture.SeedPlay(track, DateTime.UtcNow);

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.TopGenres.Count.ShouldBe(2);
        result.TopGenres.Single(g => g.Name == "Rock").Count.ShouldBe(1);
        result.TopGenres.Single(g => g.Name == "Pop").Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetStats_TopArtists_CountsPlaysPerArtist()
    {
        using var fixture = new DbTestFixture();
        var artist = fixture.SeedArtist("Test Artist");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");

        using (var ctx = fixture.CreateContext())
        {
            var trackEntity = ctx.Tracks.Single(t => t.Id == track.Id);
            trackEntity.Artists.Add(ctx.Artists.Single(a => a.Id == artist.Id));
            ctx.SaveChanges();
        }

        fixture.SeedPlay(track, DateTime.UtcNow);
        fixture.SeedPlay(track, DateTime.UtcNow);

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.TopArtists.ShouldHaveSingleItem();
        result.TopArtists.Single().Name.ShouldBe("Test Artist");
        result.TopArtists.Single().Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------ time range

    [Fact]
    public async Task GetStats_Range_DropsPlaysOlderThanTheWindow()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        fixture.SeedPlay(track, DateTime.UtcNow.AddHours(-1));
        fixture.SeedPlay(track, DateTime.UtcNow.AddDays(-3));
        fixture.SeedPlay(track, DateTime.UtcNow.AddDays(-90));

        var repo = fixture.CreateStatsRepository();

        (await repo.GetStats(StatsRange.Day, null, CancellationToken.None)).PlayHistory.Count.ShouldBe(1);
        (await repo.GetStats(StatsRange.Week, null, CancellationToken.None)).PlayHistory.Count.ShouldBe(2);
        (await repo.GetStats(StatsRange.All, null, CancellationToken.None)).PlayHistory.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetStats_Range_AlsoNarrowsTheTopBreakdowns()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var recent = fixture.SeedTrack(album, "Recent", "/music/recent.mp3");
        var ancient = fixture.SeedTrack(album, "Ancient", "/music/ancient.mp3");
        LinkArtist(fixture, recent.Id, "This Week");
        LinkArtist(fixture, ancient.Id, "Long Ago");

        fixture.SeedPlay(recent, DateTime.UtcNow.AddHours(-2));
        fixture.SeedPlay(ancient, DateTime.UtcNow.AddDays(-400));

        var repo = fixture.CreateStatsRepository();
        var week = await repo.GetStats(StatsRange.Week, null, CancellationToken.None);

        week.TopArtists.ShouldHaveSingleItem();
        week.TopArtists.Single().Name.ShouldBe("This Week");

        var all = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        all.TopArtists.Count.ShouldBe(2);
    }

    // ----------------------------------------------------------------------- scope

    [Fact]
    public async Task GetStats_WithUserId_CountsOnlyThatUsersPlays()
    {
        using var fixture = new DbTestFixture();
        var mine = fixture.SeedUser("mine");
        var theirs = fixture.SeedUser("theirs");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        LinkArtist(fixture, track.Id, "Test Artist");

        fixture.SeedPlay(track, DateTime.UtcNow, mine);
        fixture.SeedPlay(track, DateTime.UtcNow, theirs);
        fixture.SeedPlay(track, DateTime.UtcNow); // recorded before plays carried a user

        var repo = fixture.CreateStatsRepository();

        var justMe = await repo.GetStats(StatsRange.All, mine.Id, CancellationToken.None);
        justMe.PlayHistory.ShouldHaveSingleItem();
        justMe.TopArtists.Single().Count.ShouldBe(1);

        var everyone = await repo.GetStats(StatsRange.All, null, CancellationToken.None);
        everyone.PlayHistory.Count.ShouldBe(3);
        everyone.TopArtists.Single().Count.ShouldBe(3);
    }

    // --------------------------------------------------------------- library totals

    [Fact]
    public async Task GetStats_Library_ReturnsTrackAlbumArtistCounts()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedArtist("Test Artist");
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "Track One", "/music/one.mp3");
        fixture.SeedTrack(album, "Track Two", "/music/two.mp3");

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.Library.TrackCount.ShouldBe(2);
        result.Library.AlbumCount.ShouldBe(1);
        result.Library.ArtistCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetStats_Library_SumsTotalDuration()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "Track One", "/music/one.mp3", duration: TimeSpan.FromMinutes(3));
        fixture.SeedTrack(album, "Track Two", "/music/two.mp3", duration: TimeSpan.FromMinutes(4));

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.Library.TotalDuration.ShouldBe(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task GetStats_Library_CountsPossiblyCorruptTracks()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var goodTrack = fixture.SeedTrack(album, "Good Track", "/music/good.mp3");
        var badTrack = fixture.SeedTrack(album, "Bad Track", "/music/bad.mp3");

        using (var ctx = fixture.CreateContext())
        {
            ctx.Tracks.Single(t => t.Id == badTrack.Id).PossiblyCorrupt = true;
            await ctx.SaveChangesAsync();
        }

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.Library.PossiblyCorruptCount.ShouldBe(1);
        goodTrack.PossiblyCorrupt.ShouldBeFalse();
    }

    [Fact]
    public async Task GetStats_Library_GroupsFormatBreakdownByFormatIncludingUnknown()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var flacTrack = fixture.SeedTrack(album, "Flac Track", "/music/one.flac");
        var mp3Track = fixture.SeedTrack(album, "Mp3 Track", "/music/two.mp3");
        fixture.SeedTrack(album, "Unknown Track", "/music/three.mp3");

        using (var ctx = fixture.CreateContext())
        {
            ctx.Tracks.Single(t => t.Id == flacTrack.Id).Format = "FLAC";
            ctx.Tracks.Single(t => t.Id == mp3Track.Id).Format = "MP3";
            await ctx.SaveChangesAsync();
        }

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.Library.FormatBreakdown.Single(f => f.Name == "FLAC").Count.ShouldBe(1);
        result.Library.FormatBreakdown.Single(f => f.Name == "MP3").Count.ShouldBe(1);
        result.Library.FormatBreakdown.Single(f => f.Name == "Unknown").Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetStats_Library_CountsTracksPerArtistAndGenre_NotPlays()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var played = fixture.SeedTrack(album, "Played", "/music/played.mp3");
        var neverPlayed = fixture.SeedTrack(album, "Never Played", "/music/never.mp3");
        var alsoNeverPlayed = fixture.SeedTrack(album, "Also Never", "/music/also.mp3");

        var band = fixture.SeedArtist("The Band");
        var rock = fixture.SeedGenre("Rock");

        using (var ctx = fixture.CreateContext())
        {
            foreach (var id in new[] { played.Id, neverPlayed.Id, alsoNeverPlayed.Id })
            {
                var track = ctx.Tracks.Single(t => t.Id == id);
                track.Artists.Add(ctx.Artists.Single(a => a.Id == band.Id));
                track.Genres.Add(ctx.Genres.Single(g => g.Id == rock.Id));
            }

            ctx.SaveChanges();
        }

        // Played many times over, which must not inflate the track counts.
        fixture.SeedPlay(played, DateTime.UtcNow);
        fixture.SeedPlay(played, DateTime.UtcNow);
        fixture.SeedPlay(played, DateTime.UtcNow);

        var repo = fixture.CreateStatsRepository();
        var result = await repo.GetStats(StatsRange.All, null, CancellationToken.None);

        result.Library.ArtistBreakdown.Single().Name.ShouldBe("The Band");
        result.Library.ArtistBreakdown.Single().Count.ShouldBe(3);
        result.Library.GenreBreakdown.Single().Count.ShouldBe(3);

        // The play-ranked list is the other metric, and counts plays.
        result.TopArtists.Single().Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetStats_Library_TrackBreakdowns_IgnoreRangeAndScope()
    {
        using var fixture = new DbTestFixture();
        var user = fixture.SeedUser("someone");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        LinkArtist(fixture, track.Id, "The Band");
        fixture.SeedPlay(track, DateTime.UtcNow.AddDays(-400), user);

        var repo = fixture.CreateStatsRepository();
        var narrow = await repo.GetStats(StatsRange.Day, user.Id, CancellationToken.None);

        // Nothing was played today, so the play ranking is empty - the collection is still there.
        narrow.TopArtists.ShouldBeEmpty();
        narrow.Library.ArtistBreakdown.Single().Name.ShouldBe("The Band");
        narrow.Library.ArtistBreakdown.Single().Count.ShouldBe(1);
    }

    // -------------------------------------------------------------- library growth

    [Fact]
    public async Task GetStats_Growth_AccumulatesTracksPerMonthAndFillsIdleMonths()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var threeMonthsAgo = thisMonth.AddMonths(-3);

        fixture.SeedTrack(album, "Old One", "/music/old-one.mp3", addedDate: threeMonthsAgo.AddDays(2));
        fixture.SeedTrack(album, "Old Two", "/music/old-two.mp3", addedDate: threeMonthsAgo.AddDays(9));
        fixture.SeedTrack(album, "New One", "/music/new-one.mp3", addedDate: thisMonth.AddDays(1));

        var repo = fixture.CreateStatsRepository();
        var growth = (await repo.GetStats(StatsRange.All, null, CancellationToken.None)).Library.Growth.ToArray();

        growth.Length.ShouldBe(4); // three months ago .. this month, nothing skipped
        growth[0].Month.ShouldBe(threeMonthsAgo);
        growth[0].Added.ShouldBe(2);
        growth[0].TotalTracks.ShouldBe(2);
        growth[1].Added.ShouldBe(0);
        growth[1].TotalTracks.ShouldBe(2); // an idle month holds the running total flat
        growth[^1].Month.ShouldBe(thisMonth);
        growth[^1].Added.ShouldBe(1);
        growth[^1].TotalTracks.ShouldBe(3);
    }

    [Fact]
    public async Task GetStats_Growth_FoldsVeryOldAddedDatesIntoTheFirstPointsTotal()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // A CSV import can carry added dates from decades back; the curve stays bounded.
        fixture.SeedTrack(album, "Ancient", "/music/ancient.mp3", addedDate: thisMonth.AddYears(-30));
        fixture.SeedTrack(album, "New", "/music/new.mp3", addedDate: thisMonth.AddDays(3));

        var repo = fixture.CreateStatsRepository();
        var growth = (await repo.GetStats(StatsRange.All, null, CancellationToken.None)).Library.Growth.ToArray();

        growth.Length.ShouldBe(120);
        growth[0].TotalTracks.ShouldBe(1); // the ancient track, carried in rather than plotted
        growth[0].Added.ShouldBe(0);
        growth[^1].TotalTracks.ShouldBe(2);
    }

    private static void LinkArtist(DbTestFixture fixture, uint trackId, string artistName)
    {
        var artist = fixture.SeedArtist(artistName);

        using var ctx = fixture.CreateContext();
        var track = ctx.Tracks.Single(t => t.Id == trackId);
        track.Artists.Add(ctx.Artists.Single(a => a.Id == artist.Id));
        ctx.SaveChanges();
    }
}
