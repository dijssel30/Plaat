using Plaat.Contract.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class TrackRepositoryPagingTests
{
    [Fact]
    public async Task GetTracksPage_PagesAndReportsTotal()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        for (var i = 0; i < 25; i++)
        {
            fixture.SeedTrack(album, $"Track {i:D2}", $"/music/{i}.mp3");
        }

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(new TrackQuery(Skip: 10, Take: 5), CancellationToken.None);

        page.TotalCount.ShouldBe(25);
        page.Items.Count.ShouldBe(5);
        page.Items[0].Title.ShouldBe("Track 10");
        page.Items[4].Title.ShouldBe("Track 14");
    }

    [Fact]
    public async Task GetTracksPage_Search_IsDiacriticInsensitive()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        fixture.SeedTrack(album, "Blue Öyster Cult Anthem", "/music/a.mp3");
        fixture.SeedTrack(album, "Something else", "/music/b.mp3");

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(new TrackQuery(Search: "oyster"), CancellationToken.None);

        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Blue Öyster Cult Anthem");
    }

    [Fact]
    public async Task GetTracksPage_Search_RanksExactArtistMatch_AboveArtistThatOnlyContainsTheQuery()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        fixture.SeedTrack(album, "A Rayel Song", "/music/a.mp3", artistName: "Andrew Rayel");
        fixture.SeedTrack(album, "Escapism", "/music/b.mp3", artistName: "RAYE");

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(new TrackQuery(Search: "RAYE"), CancellationToken.None);

        page.Items.Select(t => t.Title).ShouldBe(["Escapism", "A Rayel Song"]);
    }

    [Fact]
    public async Task GetTracksPage_Search_MatchesWordsAcrossTitleAndArtist_RegardlessOfOrder()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        fixture.SeedTrack(album, "Escapism", "/music/a.mp3", artistName: "RAYE");
        fixture.SeedTrack(album, "Something else", "/music/b.mp3");

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(new TrackQuery(Search: "escapism raye"), CancellationToken.None);

        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Escapism");
    }

    [Fact]
    public async Task GetTracksPage_Search_TrimsTrailingWhitespace()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        fixture.SeedTrack(album, "Escapism", "/music/a.mp3", artistName: "RAYE");

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(new TrackQuery(Search: "raye "), CancellationToken.None);

        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Escapism");
    }

    [Fact]
    public async Task GetTracksPage_SortByTitleDescending()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        fixture.SeedTrack(album, "Apple", "/music/a.mp3");
        fixture.SeedTrack(album, "Cherry", "/music/c.mp3");
        fixture.SeedTrack(album, "Banana", "/music/b.mp3");

        var repo = fixture.CreateTrackRepository();

        var page = await repo.GetTracksPage(
            new TrackQuery(Sort: TrackSortField.Title, Descending: true), CancellationToken.None);

        page.Items.Select(t => t.Title).ShouldBe(["Cherry", "Banana", "Apple"]);
    }

    [Fact]
    public async Task GetByIds_PreservesRequestedOrder_AndSkipsUnknown()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        var a = fixture.SeedTrack(album, "A", "/music/a.mp3");
        var b = fixture.SeedTrack(album, "B", "/music/b.mp3");
        var c = fixture.SeedTrack(album, "C", "/music/c.mp3");

        var repo = fixture.CreateTrackRepository();

        var result = await repo.GetByIds([c.Id, 9999, a.Id, b.Id], CancellationToken.None);

        result.Select(t => t.Title).ShouldBe(["C", "A", "B"]);
    }

    [Fact]
    public async Task GetRecentlyPlayed_ReturnsDistinctMostRecentFirst()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        var older = fixture.SeedTrack(album, "Older", "/music/o.mp3");
        var newer = fixture.SeedTrack(album, "Newer", "/music/n.mp3");

        fixture.SeedPlay(older, DateTime.UtcNow.AddHours(-3));
        fixture.SeedPlay(newer, DateTime.UtcNow.AddHours(-2));
        fixture.SeedPlay(older, DateTime.UtcNow.AddHours(-1));

        var repo = fixture.CreateTrackRepository();

        var result = await repo.GetRecentlyPlayed(10, CancellationToken.None);

        result.Select(t => t.Title).ShouldBe(["Older", "Newer"]);
    }

    [Fact]
    public async Task IncreasePlayCount_DoesNotEvictTrackListCache()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Album");
        var track = fixture.SeedTrack(album, "One", "/music/one.mp3");
        var repo = fixture.CreateTrackRepository();

        _ = await repo.GetAll(CancellationToken.None); // warm the cache

        fixture.SeedTrack(album, "Two", "/music/two.mp3"); // added out-of-band

        await repo.IncreasePlayCount(track.Id, null, CancellationToken.None);

        // The play-count bump must not evict the big track list - it stays the cached snapshot.
        var afterBump = await repo.GetAll(CancellationToken.None);
        afterBump.Count.ShouldBe(1);
    }
}
