using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Pages;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class StatsComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public StatsComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });

        _client.GetStats(Arg.Any<StatsRange>(), Arg.Any<StatsScope>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Stats(evenings: 3)));
    }

    private static StatsDto Stats(int evenings)
    {
        return new()
        {
            // 21:00 local, expressed the way the API stores and sends it - in UTC.
            PlayHistory = Enumerable.Range(0, evenings)
            .Select(i => DateTime.Today.AddHours(21).AddDays(-i).ToUniversalTime())
            .ToArray(),
            TopArtists = [new NamedCountDto { Name = "The Band", Count = 7 }],
            TopGenres = [new NamedCountDto { Name = "Rock", Count = 7 }],
            Library = new LibraryStatsDto
            {
                TrackCount = 120,
                AlbumCount = 12,
                ArtistCount = 8,
                PossiblyCorruptCount = 0,
                TotalDuration = TimeSpan.FromHours(9),
                FormatBreakdown = [new NamedCountDto { Name = "FLAC", Count = 120 }],
                ArtistBreakdown = [new NamedCountDto { Name = "Shelf Filler", Count = 64 }],
                GenreBreakdown = [new NamedCountDto { Name = "Ambient", Count = 41 }],
                Growth =
            [
                new LibraryGrowthPointDto { Month = new DateTime(2026, 1, 1), Added = 100, TotalTracks = 100 },
                new LibraryGrowthPointDto { Month = new DateTime(2026, 2, 1), Added = 20, TotalTracks = 120 }
            ]
            }
        };
    }

    [Fact]
    public void OnInit_LoadsEveryPlayEverForEveryone()
    {
        var cut = Render<Stats>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("The Band");
            cut.Markup.ShouldContain("Rock");
            cut.Markup.ShouldContain("FLAC");
        });

        // Narrowing is opt-in: an artist last played a year ago must still be on the page when it
        // opens, exactly as it was before the range switch existed.
        _client.Received(1).GetStats(StatsRange.All, StatsScope.Everyone, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChoosingARange_ReloadsFromTheServerForThatRange()
    {
        var cut = Render<Stats>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The Band"));

        cut.FindAll("[aria-label='Time range'] button").Single(b => b.TextContent == "Year").Click();

        cut.WaitForAssertion(() =>
            _client.Received(1).GetStats(StatsRange.Year, StatsScope.Everyone, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ChoosingAScope_ReloadsFromTheServerForThatScope()
    {
        var cut = Render<Stats>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The Band"));

        cut.FindAll("[aria-label='Whose plays to count'] button").Single(b => b.TextContent == "Me").Click();

        cut.WaitForAssertion(() =>
            _client.Received(1).GetStats(StatsRange.All, StatsScope.Me, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void TimeOfDay_BucketsPlaysByLocalHourAndNamesThePeak()
    {
        var cut = Render<Stats>();

        cut.WaitForAssertion(() =>
        {
            // Three plays all at 21:00 local, sent as UTC - they must land on one bucket, not
            // scattered across whatever the UTC offset happens to be.
            cut.Markup.ShouldContain("21:00: 3 plays");
            cut.Markup.ShouldContain("busiest around 21:00");
        });
    }

    [Fact]
    public void LibraryGrowth_PlotsTheRunningTotal()
    {
        var cut = Render<Stats>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Feb 2026: 120 tracks");
            cut.Markup.ShouldContain("since Jan 2026");
        });
    }

    [Fact]
    public void TrackBreakdowns_AreShownSeparatelyFromThePlayRankings()
    {
        var cut = Render<Stats>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Artists by tracks");
            cut.Markup.ShouldContain("Shelf Filler: 64 tracks");
            cut.Markup.ShouldContain("Genres by tracks");
            cut.Markup.ShouldContain("Ambient: 41 tracks");

            // The other ranking is plays, and now says so on both the heading and the row.
            cut.Markup.ShouldContain("Most played artists");
            cut.Markup.ShouldContain("The Band: 7 plays");
        });
    }

    [Fact]
    public void TrackBreakdowns_SurviveARangeThatEmptiesThePlayRankings()
    {
        _client.GetStats(Arg.Any<StatsRange>(), Arg.Any<StatsScope>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new StatsDto
            {
                PlayHistory = [],
                TopArtists = [],
                TopGenres = [],
                Library = Stats(evenings: 0).Library
            }));

        var cut = Render<Stats>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No plays recorded yet.");
            cut.Markup.ShouldContain("Shelf Filler");
            cut.Markup.ShouldContain("FLAC");
        });
    }

    [Fact]
    public void WithNoPlaysInRange_SaysSoPerScope()
    {
        _client.GetStats(Arg.Any<StatsRange>(), Arg.Any<StatsScope>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Stats(evenings: 0)));

        var cut = Render<Stats>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No plays recorded yet."));

        cut.FindAll("[aria-label='Whose plays to count'] button").Single(b => b.TextContent == "Me").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No plays of yours recorded yet."));

        cut.FindAll("[aria-label='Time range'] button").Single(b => b.TextContent == "Month").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No plays of yours in the last 30 days."));
    }
}
