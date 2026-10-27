using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class TrackRepositoryTests
{
    [Fact]
    public async Task GetAll_WhenEmpty_ReturnsEmptyCollection()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        var result = await repo.GetAll(CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAll_WhenTracksExist_ReturnsAll()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "Track One", "/music/one.mp3");
        fixture.SeedTrack(album, "Track Two", "/music/two.mp3");

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetAll(CancellationToken.None);

        result.Count.ShouldBe(2);
        result.Select(t => t.Title).ShouldContain("Track One");
        result.Select(t => t.Title).ShouldContain("Track Two");
    }

    [Fact]
    public async Task GetAll_IncludesAlbumTitle()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("My Album");
        fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetAll(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.First().Album.ShouldBe("My Album");
    }

    [Fact]
    public async Task GetAll_TrackWithNoLoudnessMeasurement_HasUnchangedAttenuationFactor()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetAll(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.First().AttenuationFactor.ShouldBe(1.0);
    }

    [Fact]
    public async Task GetAll_TrackLouderThanTarget_HasAttenuatedFactor()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreateTrackRepository();
        await repo.SetIntegratedLoudness(track.Id, -4f, CancellationToken.None);

        var result = await repo.GetAll(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.First().AttenuationFactor.ShouldBeLessThan(1.0);
    }

    [Fact]
    public async Task GetTrackIdsNeedingLoudnessAnalysis_OnlyReturnsUnmeasuredTracks()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var measured = fixture.SeedTrack(album, "Measured", "/music/measured.mp3");
        var unmeasured = fixture.SeedTrack(album, "Unmeasured", "/music/unmeasured.mp3");

        var repo = fixture.CreateTrackRepository();
        await repo.SetIntegratedLoudness(measured.Id, -10f, CancellationToken.None);

        var result = await repo.GetTrackIdsNeedingLoudnessAnalysis(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.ShouldContain(unmeasured.Id);
    }

    [Fact]
    public async Task SetIntegratedLoudness_WhenTrackDoesNotExist_DoesNothing()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        await Should.NotThrowAsync(() => repo.SetIntegratedLoudness(999, -10f, CancellationToken.None));
    }

    [Fact]
    public async Task GetTrimPoints_WhenNoneSet_ReturnsNullFields()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", duration: TimeSpan.FromMinutes(3));

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetTrimPoints(track.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.TrimStart.ShouldBeNull();
        result.TrimEnd.ShouldBeNull();
    }

    [Fact]
    public async Task GetTrimPoints_WhenTrackDoesNotExist_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        var result = await repo.GetTrimPoints(999, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetTrimPoints_PersistsAndIsReflectedByGetTrimPoints()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", duration: TimeSpan.FromMinutes(3));

        var repo = fixture.CreateTrackRepository();
        await repo.SetTrimPoints(track.Id, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(150), CancellationToken.None);

        var result = await repo.GetTrimPoints(track.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.TrimStart.ShouldBe(TimeSpan.FromSeconds(10));
        result.TrimEnd.ShouldBe(TimeSpan.FromSeconds(150));
    }

    [Fact]
    public async Task SetTrimPoints_IsReflectedInGetAll()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", duration: TimeSpan.FromMinutes(3));

        var repo = fixture.CreateTrackRepository();
        await repo.SetTrimPoints(track.Id, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(150), CancellationToken.None);

        var result = await repo.GetAll(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.First().TrimStart.ShouldBe(TimeSpan.FromSeconds(10));
        result.First().TrimEnd.ShouldBe(TimeSpan.FromSeconds(150));
    }

    [Fact]
    public async Task SetTrimPoints_WhenTrackDoesNotExist_DoesNothing()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        await Should.NotThrowAsync(() => repo.SetTrimPoints(999, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None));
    }

    [Fact]
    public async Task GetPath_WhenTrackExists_ReturnsPath()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", "/music/song.mp3");

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetPath(track.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Path.ShouldBe("/music/song.mp3");
        result.MimeType.ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task IncreasePlayCount_WhenTrackExists_IncrementsCount()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreateTrackRepository();
        await repo.IncreasePlayCount(track.Id, null, CancellationToken.None);

        using var ctx = fixture.CreateContext();
        var updated = ctx.Tracks.Find(track.Id);
        updated.ShouldNotBeNull();
        updated.PlayCount.ShouldBe(1u);
    }

    [Fact]
    public async Task IncreasePlayCount_RecordsCallingUserOnPlay()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        var user = fixture.SeedUser("alice");

        var repo = fixture.CreateTrackRepository();
        await repo.IncreasePlayCount(track.Id, user.Id, CancellationToken.None);

        using var ctx = fixture.CreateContext();
        var play = ctx.Plays.Single(p => p.TrackId == track.Id);
        play.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task IncreasePlayCount_WhenTrackDoesNotExist_DoesNotThrow()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        var exception = await Record.ExceptionAsync(() =>
            repo.IncreasePlayCount(9999, null, CancellationToken.None));

        exception.ShouldBeNull();
    }

    [Fact]
    public async Task GetCoverArt_WhenTrackHasNoCoverArt_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetCoverArt(track.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetTrackDetails_WhenTrackDoesNotExist_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateTrackRepository();

        var result = await repo.GetTrackDetails(9999, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetTrackDetails_WhenTrackExists_ReturnsStatsAndLocations()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", "/music/one.mp3");
        fixture.SeedTrackLocation(track, "/music/one.flac", isDefault: true);

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetTrackDetails(track.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Title.ShouldBe("A Track");
        result.Locations.Count.ShouldBe(2);
        result.Locations.Single(l => l.Path == "/music/one.flac").IsDefault.ShouldBeTrue();
        result.Locations.Single(l => l.Path == "/music/one.mp3").IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task GetTrackDetails_IncludesPlayHistory()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track");
        var playDate = new DateTime(2026, 1, 1, 12, 0, 0);
        fixture.SeedPlay(track, playDate);

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetTrackDetails(track.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.PlayHistory.ShouldHaveSingleItem();
        result.PlayHistory.Single().ShouldBe(playDate);
    }

    [Fact]
    public async Task SetDefaultLocation_MarksOnlyChosenLocationAsDefault()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", "/music/one.mp3");
        var flacLocation = fixture.SeedTrackLocation(track, "/music/one.flac", isDefault: true);

        var repo = fixture.CreateTrackRepository();

        using var ctxBefore = fixture.CreateContext();
        var mp3Location = ctxBefore.TrackLocations.Single(l => l.Path == "/music/one.mp3");

        var result = await repo.SetDefaultLocation(track.Id, mp3Location.Id, CancellationToken.None);

        result.ShouldBeTrue();
        using var ctx = fixture.CreateContext();
        ctx.TrackLocations.Single(l => l.Id == mp3Location.Id).Default.ShouldBeTrue();
        ctx.TrackLocations.Single(l => l.Id == flacLocation.Id).Default.ShouldBeFalse();
    }

    [Fact]
    public async Task SetDefaultLocation_WhenLocationBelongsToDifferentTrack_ReturnsFalseAndLeavesDefaultsUnchanged()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", "/music/one.mp3");
        var flacLocation = fixture.SeedTrackLocation(track, "/music/one.flac", isDefault: true);
        var otherTrack = fixture.SeedTrack(album, "Other Track", "/music/other.mp3");

        var repo = fixture.CreateTrackRepository();

        using var ctxBefore = fixture.CreateContext();
        var otherLocation = ctxBefore.TrackLocations.Single(l => l.Path == "/music/other.mp3");

        var result = await repo.SetDefaultLocation(track.Id, otherLocation.Id, CancellationToken.None);

        result.ShouldBeFalse();
        using var ctx = fixture.CreateContext();
        ctx.TrackLocations.Single(l => l.Id == flacLocation.Id).Default.ShouldBeTrue();
    }

    [Fact]
    public async Task GetPossiblyCorrupt_OnlyReturnsFlaggedTracks()
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

        var repo = fixture.CreateTrackRepository();
        var result = await repo.GetPossiblyCorrupt(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.Single().Title.ShouldBe("Bad Track");
        result.Single().Paths.ShouldContain("/music/bad.mp3");
        result.ShouldNotContain(t => t.Id == goodTrack.Id);
    }
}
