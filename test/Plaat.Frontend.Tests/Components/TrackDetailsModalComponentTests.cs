using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The track info modal: details, rating, admin trim editing, default location and play history.
/// </summary>
public sealed class TrackDetailsModalComponentTests : BunitContext, IAsyncLifetime
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();

    public TrackDetailsModalComponentTests()
    {
        Services.AddSingleton(_client);
        Services.AddMudServices();
        Services.AddSingleton(_snackbar);
        Services.AddLogging();
        Services.AddSingleton<TrackRatingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }

    private static TrackDetailsDto Details(
        uint id = 1,
        TimeSpan? trimStart = null,
        TimeSpan? trimEnd = null,
        IReadOnlyCollection<DateTime>? plays = null,
        long size = 5 * 1024 * 1024,
        float? lufs = null,
        string? lyrics = null,
        bool corrupt = false,
        TimeSpan? duration = null)
    {
        return new TrackDetailsDto
        {
            Id = id,
            Title = "Song Title",
            Artists = [new ArtistOnlyDto { Id = 1, Name = "Artist" }],
            Album = "Album",
            AlbumId = 2,
            PlayCount = 3,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2000,
            Duration = duration ?? TimeSpan.FromMinutes(4),
            TrimStart = trimStart,
            TrimEnd = trimEnd,
            IntegratedLoudnessLufs = lufs,
            AddedDate = new DateTime(2020, 1, 1),
            PossiblyCorrupt = corrupt,
            BitRate = 320,
            SampleRate = 44100,
            BitsPerSample = 16,
            FileHash = "HASH",
            FileSizeBytes = size,
            MimeType = "audio/flac",
            Format = "FLAC",
            Lyrics = lyrics,
            Locations =
            [
                new TrackLocationDto { Id = 10, Path = "/music/a.flac", IsSupportedFormat = true, IsDefault = true },
                new TrackLocationDto { Id = 11, Path = "/music/b.m4a", IsSupportedFormat = true, IsDefault = false }
            ],
            PlayHistory = plays ?? []
        };
    }

    private IRenderedComponent<TrackDetailsModal> RenderModal(TrackDetailsDto details, bool admin = false, Action? onClose = null)
    {
        _client.GetTrackDetails(details.Id, Arg.Any<CancellationToken>()).Returns(details);
        var auth = AddAuthorization();
        auth.SetAuthorized("user");
        if (admin)
        {
            auth.SetRoles("Admin");
        }

        return Render<TrackDetailsModal>(p => p
            .Add(x => x.TrackId, details.Id)
            .Add(x => x.OnClose, onClose ?? (() => { })));
    }

    private static void EnterTrim(IRenderedComponent<TrackDetailsModal> cut, string start, string end)
    {
        cut.FindAll("input.form-control-sm")[0].Change(start);
        cut.FindAll("input.form-control-sm")[1].Change(end);
        cut.Find("button.btn-primary.btn-sm").Click();
    }

    [Fact]
    public void WithoutATrack_RendersNothing()
    {
        Render<TrackDetailsModal>().Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void ShowsTheTracksDetails()
    {
        _client.GetTrackRating(1, Arg.Any<CancellationToken>()).Returns(4);

        var cut = RenderModal(Details(lufs: -9.5f, lyrics: "la la", corrupt: true));

        cut.WaitForAssertion(() => cut.Find("h4").TextContent.ShouldBe("Song Title"));
        cut.Markup.ShouldMatch(@"44[.,]1 kHz");
        cut.Markup.ShouldContain("5 MB");
        cut.Markup.ShouldMatch(@"-9[.,]5 LUFS");
        cut.Markup.ShouldContain("04:00");
        cut.Markup.ShouldContain("Possibly corrupt file");
        cut.Find(".track-lyrics").TextContent.ShouldBe("la la");
        cut.Markup.ShouldContain("No plays recorded yet.");
        cut.FindAll("h6").ShouldNotContain(h => h.TextContent.Contains("trim"));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void FormatsFileSizes(long bytes, string expected)
    {
        var cut = RenderModal(Details(size: bytes));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain(expected));
        cut.Markup.ShouldContain("Not analyzed");
    }

    [Fact]
    public void Close_ClearsAndNotifiesTheParent()
    {
        var closed = 0;
        var cut = RenderModal(Details(), onClose: () => closed++);
        cut.WaitForAssertion(() => cut.Find("h4"));

        cut.Find(".btn-close").Click();

        closed.ShouldBe(1);
    }

    [Fact]
    public void ChangingToAnotherTrack_LoadsItAndClearingTheTrackHidesTheModal()
    {
        var cut = RenderModal(Details(id: 1));
        cut.WaitForAssertion(() => cut.Find("h4"));
        _client.GetTrackDetails(2, Arg.Any<CancellationToken>()).Returns(Details(id: 2));

        cut.Render(p => p.Add(x => x.TrackId, 1u));
        cut.Render(p => p.Add(x => x.TrackId, 2u));
        cut.Render(p => p.Add(x => x.TrackId, (uint?)null));

        _client.Received(1).GetTrackDetails(1, Arg.Any<CancellationToken>());
        _client.Received(1).GetTrackDetails(2, Arg.Any<CancellationToken>());
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void LoadingTheRatingFails_ShowsNoStars()
    {
        _client.GetTrackRating(1, Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new HttpRequestException()));

        var cut = RenderModal(Details());

        cut.WaitForAssertion(() => cut.Find("h4"));
    }

    [Fact]
    public void SetDefaultLocation_SavesAndReloads()
    {
        var cut = RenderModal(Details());
        cut.WaitForAssertion(() => cut.Find("h4"));

        cut.Find("tbody button").Click();

        _client.Received(1).SetDefaultTrackLocation(1, 11, Arg.Any<CancellationToken>());
        _client.Received(2).GetTrackDetails(1, Arg.Any<CancellationToken>());
    }

    // ---- ratings ----

    [Fact]
    public void Rating_SaveFailuresRollBackAndSayWhy()
    {
        _client.SetTrackRating(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false, true);
        var cut = RenderModal(Details());
        cut.WaitForAssertion(() => cut.Find("h4"));
        var stars = cut.FindComponent<StarRating>();

        cut.InvokeAsync(() => stars.Instance.ValueChanged.InvokeAsync(3));
        _snackbar.Received(1).Add("Couldn't save the rating: this track no longer exists.", Severity.Error);

        _client.SetTrackRating(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        cut.InvokeAsync(() => stars.Instance.ValueChanged.InvokeAsync(2));
        _snackbar.Received(1).Add("Couldn't save the rating.", Severity.Error);
        stars.Instance.Value.ShouldBe(0);
    }

    [Fact]
    public void Rating_Saved_KeepsTheNewValue()
    {
        _client.SetTrackRating(1, 5, Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderModal(Details());
        cut.WaitForAssertion(() => cut.Find("h4"));
        var stars = cut.FindComponent<StarRating>();

        cut.InvokeAsync(() => stars.Instance.ValueChanged.InvokeAsync(5));

        stars.Instance.Value.ShouldBe(5);
    }

    // ---- trim (admin) ----

    [Fact]
    public void Trim_ShowsTheExistingPointsToAdmins()
    {
        var cut = RenderModal(Details(trimStart: TimeSpan.FromSeconds(5), trimEnd: TimeSpan.FromSeconds(200)), admin: true);

        cut.WaitForAssertion(() => cut.FindAll("input.form-control-sm")[0].GetAttribute("value").ShouldBe("00:05"));
        cut.FindAll("input.form-control-sm")[1].GetAttribute("value").ShouldBe("03:20");
    }

    [Fact]
    public void Trim_PointsBeyondAnHour_AreShownAndSavedWithoutLosingTheHours()
    {
        var trimEnd = new TimeSpan(1, 15, 30);
        _client.SetTrackTrim(1, Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderModal(Details(trimEnd: trimEnd, duration: TimeSpan.FromMinutes(90)), admin: true);
        cut.WaitForAssertion(() => cut.FindAll("input.form-control-sm")[1].GetAttribute("value").ShouldBe("75:30"));
        cut.Markup.ShouldContain("90:00");

        cut.Find("button.btn-primary.btn-sm").Click();

        _client.Received(1).SetTrackTrim(1, null, trimEnd, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1:2:3", "", "Enter times as mm:ss.")]
    [InlineData("", "abc", "Enter times as mm:ss.")]
    [InlineData("0:60", "", "Enter times as mm:ss.")]
    [InlineData("-1:00", "", "Enter times as mm:ss.")]
    [InlineData("2:00", "1:00", "Start must be before end.")]
    public void Trim_InvalidInput_IsRejectedWithoutSaving(string start, string end, string error)
    {
        var cut = RenderModal(Details(), admin: true);
        cut.WaitForAssertion(() => cut.Find("h4"));

        EnterTrim(cut, start, end);

        cut.Find("p.text-danger").TextContent.ShouldBe(error);
        _client.DidNotReceiveWithAnyArgs().SetTrackTrim(0, null, null, CancellationToken.None);
    }

    [Fact]
    public void Trim_ValidInput_SavesAndReloads()
    {
        _client.SetTrackTrim(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(125), Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderModal(Details(), admin: true);
        cut.WaitForAssertion(() => cut.Find("h4"));

        EnterTrim(cut, "0:05", "2:05");

        _client.Received(2).GetTrackDetails(1, Arg.Any<CancellationToken>());
        cut.FindAll("p.text-danger").ShouldBeEmpty();
    }

    [Fact]
    public void Trim_Clear_SavesEmptyPoints()
    {
        _client.SetTrackTrim(1, null, null, Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderModal(Details(trimStart: TimeSpan.FromSeconds(5)), admin: true);
        cut.WaitForAssertion(() => cut.Find("h4"));

        cut.Find("button.btn-outline-secondary").Click();

        _client.Received(1).SetTrackTrim(1, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Trim_TrackGoneOrSaveFailing_TellsTheUser()
    {
        _client.SetTrackTrim(1, Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(false);
        var cut = RenderModal(Details(), admin: true);
        cut.WaitForAssertion(() => cut.Find("h4"));

        EnterTrim(cut, "0:05", "");
        _snackbar.Received(1).Add("Couldn't save the trim points: this track no longer exists.", Severity.Error);

        _client.SetTrackTrim(1, Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException()));
        EnterTrim(cut, "0:06", "");
        _snackbar.Received(1).Add("Couldn't save the trim points.", Severity.Error);
    }

    // ---- play history ----

    [Theory]
    [InlineData("Day")]
    [InlineData("Month")]
    [InlineData("Year")]
    [InlineData("All")]
    public void PlayHistory_IsChartedForEachRange(string range)
    {
        var now = DateTime.UtcNow;
        var cut = RenderModal(Details(plays: [now.AddHours(-1), now.AddDays(-3), now.AddYears(-2)]));
        cut.WaitForAssertion(() => cut.Find("h4"));

        cut.Find("select.form-control").Change(range);

        cut.Markup.ShouldNotContain("No plays recorded yet.");
        cut.FindComponent<MudChart<int>>().Instance.ChartLabels.ShouldNotBeEmpty();
    }
}
