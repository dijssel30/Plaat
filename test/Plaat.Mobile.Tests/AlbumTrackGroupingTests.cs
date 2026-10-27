using Plaat.Contract;
using Plaat.Mobile.ViewModels;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.Tests;

public sealed class AlbumTrackGroupingTests
{
    [Fact]
    public void BuildDiscGroups_WithOneDisc_ReturnsASingleUnnamedGroup()
    {
        var groups = AlbumTrackGrouping.BuildDiscGroups([Track(1, disc: 1), Track(2, disc: 1)]);

        groups.Count.ShouldBe(1);
        groups[0].Name.ShouldBeEmpty();
        groups[0].Select(t => t.Id).ShouldBe([1u, 2u]);
    }

    [Fact]
    public void BuildDiscGroups_WithSeveralDiscs_NamesAndOrdersThem()
    {
        var groups = AlbumTrackGrouping.BuildDiscGroups(
            [Track(3, disc: 2), Track(1, disc: 1), Track(4, disc: 3)]);

        groups.Select(g => g.Name).ShouldBe(["CD 1", "CD 2", "CD 3"]);
        groups[1].Select(t => t.Id).ShouldBe([3u]);
    }

    [Fact]
    public void BuildDiscGroups_WithUntaggedDisc_NumbersItByPositionInsteadOfCallingItCd0()
    {
        var groups = AlbumTrackGrouping.BuildDiscGroups([Track(1, disc: 0), Track(2, disc: 2)]);

        groups.Select(g => g.Name).ShouldBe(["CD 1", "CD 2"]);
    }

    [Fact]
    public void BuildYearLabel_WhenTracksAgree_ReturnsThatYear()
    {
        AlbumTrackGrouping.BuildYearLabel([Track(1, disc: 1, year: 1994), Track(2, disc: 1, year: 1994)])
            .ShouldBe("1994");
    }

    [Fact]
    public void BuildYearLabel_WhenTracksDisagree_ReturnsARange()
    {
        AlbumTrackGrouping.BuildYearLabel([Track(1, disc: 1, year: 1997), Track(2, disc: 1, year: 1994)])
            .ShouldBe("1994–1997");
    }

    [Fact]
    public void BuildYearLabel_IgnoresUntaggedTracks()
    {
        AlbumTrackGrouping.BuildYearLabel([Track(1, disc: 1, year: 0), Track(2, disc: 1, year: 1994)])
            .ShouldBe("1994");
    }

    [Fact]
    public void BuildYearLabel_WhenNothingIsTagged_ReturnsNull()
    {
        AlbumTrackGrouping.BuildYearLabel([Track(1, disc: 1, year: 0)]).ShouldBeNull();
    }

    private static TrackDto Track(uint id, uint disc, uint year = 2000)
    {
        return new TrackDto
        {
            Id = id,
            Title = $"Track {id}",
            Artists = [],
            Album = "Test Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = id,
            DiscNumber = disc,
            Year = year,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
