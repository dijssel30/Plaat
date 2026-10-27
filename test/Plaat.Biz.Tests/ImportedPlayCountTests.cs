using Xunit;
using Plaat.Biz.Scanners;
using Plaat.Db.Models;
using Shouldly;

namespace Plaat.Biz.Tests;

public sealed class ImportedPlayCountTests
{
    private static Play PlayOf(int weight, bool imported = false)
    {
        return new Play { Weight = weight, IsImported = imported, DateTime = DateTime.UtcNow };
    }

    [Fact]
    public void Apply_NoImportedPlayYet_AddsImportedPlayAndRaisesTotal()
    {
        var track = new Track { Title = "t", FileHash = "h", MimeType = "m", PlayCount = 2, Plays = [PlayOf(2)] };

        ImportedPlayCount.Apply(track, 5).ShouldBeTrue();

        track.PlayCount.ShouldBe(7u);
        track.Plays.Single(p => p.IsImported).Weight.ShouldBe(5);
    }

    [Fact]
    public void Apply_ImportedHigher_RaisesWeightByDifference()
    {
        var track = new Track { Title = "t", FileHash = "h", MimeType = "m", PlayCount = 8, Plays = [PlayOf(5, true), PlayOf(3)] };

        ImportedPlayCount.Apply(track, 9).ShouldBeTrue();

        track.PlayCount.ShouldBe(12u);
        track.Plays.Single(p => p.IsImported).Weight.ShouldBe(9);
    }

    [Fact]
    public void Apply_ImportedNotHigher_DoesNothing()
    {
        var track = new Track { Title = "t", FileHash = "h", MimeType = "m", PlayCount = 5, Plays = [PlayOf(5, true)] };

        ImportedPlayCount.Apply(track, 5).ShouldBeFalse();

        track.PlayCount.ShouldBe(5u);
    }

    [Fact]
    public void Apply_PlayedOnceWithOnlyWeightOnePlays_DoesNothing()
    {
        var track = new Track { Title = "t", FileHash = "h", MimeType = "m", PlayCount = 1, Plays = [PlayOf(1)] };

        ImportedPlayCount.Apply(track, 20).ShouldBeFalse();

        track.PlayCount.ShouldBe(1u);
        track.Plays.Count.ShouldBe(1);
    }
}
