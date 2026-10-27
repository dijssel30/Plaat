using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Components;
using Plaat.Frontend.Components.Pages;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The Settings, Search and Tracks pages.
/// </summary>
public sealed class SettingsSearchTracksComponentTests : BunitContext, IAsyncLifetime
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly MusicPlaylist _playlist = new();

    public SettingsSearchTracksComponentTests()
    {
        Services.AddSingleton(_client);
        Services.AddSingleton(_playlist);
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<MusicFileList>();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    // MudBlazor registers services that can only be disposed asynchronously.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }

    private static TrackDto Track(uint id, string title = "Song")
    {
        return new TrackDto
        {
            Id = id,
            Title = title,
            Artists = [new ArtistOnlyDto { Id = 1, Name = "Artist" }, new ArtistOnlyDto { Id = 2, Name = "Guest" }],
            Album = "Album",
            AlbumId = 3,
            PlayCount = 4,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2001,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    // ---- settings ----

    private IRenderedComponent<Settings> RenderSettings()
    {
        return Render<Settings>();
    }

    private static void FillPasswords(IRenderedComponent<Settings> cut, string current, string next, string confirm)
    {
        cut.FindAll("input[type=password]")[0].Change(current);
        cut.FindAll("input[type=password]")[1].Change(next);
        cut.FindAll("input[type=password]")[2].Change(confirm);
        cut.Find("button.btn-primary").Click();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Settings_ShowsTheStoredLoudnessSetting(bool enabled)
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>()).Returns(new CurrentUserDto("me", false, enabled));

        var cut = RenderSettings();

        cut.Find("input[type=checkbox]").HasAttribute("checked").ShouldBe(enabled);
    }

    [Fact]
    public void Settings_WhenTheUserCannotBeLoaded_DefaultsToEnabled()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>()).Returns(Task.FromException<CurrentUserDto?>(new HttpRequestException()));

        var cut = RenderSettings();

        cut.Find("input[type=checkbox]").HasAttribute("checked").ShouldBeTrue();
    }

    [Fact]
    public void Settings_UnknownUser_DefaultsToEnabled()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>()).Returns((CurrentUserDto?)null);

        RenderSettings().Find("input[type=checkbox]").HasAttribute("checked").ShouldBeTrue();
    }

    [Fact]
    public void Settings_TogglingLoudness_SavesIt()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>()).Returns(new CurrentUserDto("me", false, true));
        var cut = RenderSettings();

        cut.Find("input[type=checkbox]").Change(false);

        _client.Received(1).SetLoudnessNormalization(false, Arg.Any<CancellationToken>());
        cut.Find("input[type=checkbox]").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void Settings_TogglingLoudnessFails_RevertsTheSwitch()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>()).Returns(new CurrentUserDto("me", false, true));
        _client.SetLoudnessNormalization(false, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Yield();
            throw new HttpRequestException();
        });
        var cut = RenderSettings();

        cut.Find("input[type=checkbox]").Change(false);

        cut.WaitForAssertion(() => cut.Find("input[type=checkbox]").HasAttribute("checked").ShouldBeTrue());
    }

    [Theory]
    [InlineData("", "newpass123", "newpass123", "Current and new password are required.")]
    [InlineData("old", " ", " ", "Current and new password are required.")]
    [InlineData("old", "newpass123", "different", "New password and confirmation do not match.")]
    public void Settings_ChangePassword_ValidatesBeforeCallingTheApi(string current, string next, string confirm, string message)
    {
        var cut = RenderSettings();

        FillPasswords(cut, current, next, confirm);

        cut.Find(".alert-danger").TextContent.ShouldBe(message);
        _client.DidNotReceiveWithAnyArgs().ChangeOwnPassword(null!, CancellationToken.None);
    }

    [Fact]
    public void Settings_ChangePassword_SuccessClearsTheForm()
    {
        _client.ChangeOwnPassword(new ChangePasswordRequest("old", "newpass123"), Arg.Any<CancellationToken>()).Returns(true);
        var cut = RenderSettings();

        FillPasswords(cut, "old", "newpass123", "newpass123");

        cut.Find(".alert-success").TextContent.ShouldBe("Password updated successfully.");
        cut.FindAll("input[type=password]").ShouldAllBe(i => string.IsNullOrEmpty(i.GetAttribute("value")));
    }

    [Fact]
    public void Settings_ChangePassword_WrongCurrentPassword()
    {
        _client.ChangeOwnPassword(Arg.Any<ChangePasswordRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        var cut = RenderSettings();

        FillPasswords(cut, "wrong", "newpass123", "newpass123");

        cut.Find(".alert-danger").TextContent.ShouldBe("Current password is incorrect.");
    }

    [Fact]
    public void Settings_ChangePassword_Failure()
    {
        _client.ChangeOwnPassword(Arg.Any<ChangePasswordRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException()));
        var cut = RenderSettings();

        FillPasswords(cut, "old", "newpass123", "newpass123");

        cut.Find(".alert-danger").TextContent.ShouldBe("Failed to update password.");
    }

    // ---- search ----

    [Fact]
    public void Search_BeforeTypingEnough_ShowsTheHintWithoutSearching()
    {
        var cut = Render<Search>();

        cut.Find(".search-input").Input("a");

        cut.Find(".search-hint").TextContent.ShouldBe("Start typing to search your library.");
        _client.DidNotReceiveWithAnyArgs().Search(null!, CancellationToken.None);
    }

    [Fact]
    public void Search_ShowsEveryResultSection()
    {
        _client.Search("muse", Arg.Any<CancellationToken>()).Returns(new SearchResultsDto
        {
            Artists = [new ArtistOnlyDto { Id = 1, Name = "Muse" }],
            Albums = [new AlbumOnlyDto { Id = 2, Title = "Absolution", Artists = [new ArtistOnlyDto { Id = 1, Name = "Muse" }] }],
            Tracks = [Track(3, "Hysteria")]
        });
        var cut = Render<Search>();

        cut.Find(".search-input").Input(" muse ");

        cut.WaitForAssertion(() =>
        {
            cut.Find(".artist-link-text").TextContent.ShouldBe("Muse");
            cut.Find(".album-link-text").TextContent.ShouldContain("Absolution");
            cut.FindComponents<Stub<MusicFileList>>().ShouldHaveSingleItem();
        });
    }

    [Fact]
    public void Search_NoMatches_ShowsEmptySections()
    {
        _client.Search("zzz", Arg.Any<CancellationToken>()).Returns(new SearchResultsDto());
        var cut = Render<Search>();

        cut.Find(".search-input").Input("zzz");

        cut.WaitForAssertion(() => cut.FindAll(".search-empty").Count.ShouldBe(3));
    }

    [Fact]
    public void Search_WhileSearching_ShowsProgress()
    {
        var pending = new TaskCompletionSource<SearchResultsDto>();
        _client.Search("slow", Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = Render<Search>();

        cut.Find(".search-input").Input("slow");

        cut.WaitForAssertion(() => cut.Find(".search-hint").TextContent.ShouldBe("Searching…"));
        pending.SetResult(new SearchResultsDto());
        cut.WaitForAssertion(() => cut.FindAll(".search-empty").Count.ShouldBe(3));
    }

    [Fact]
    public void Search_Failure_ShowsNoResults()
    {
        _client.Search("boom", Arg.Any<CancellationToken>()).Returns(Task.FromException<SearchResultsDto>(new HttpRequestException()));
        var cut = Render<Search>();

        cut.Find(".search-input").Input("boom");

        cut.WaitForAssertion(() => cut.FindAll(".search-empty").Count.ShouldBe(3));
    }

    [Fact]
    public void Search_TypingQuickly_OnlySearchesTheFinalText()
    {
        _client.Search(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SearchResultsDto());
        var cut = Render<Search>();

        cut.Find(".search-input").Input("ab");
        cut.Find(".search-input").Input("abc");
        cut.Find(".search-input").Input("abc");

        cut.WaitForAssertion(() => _client.Received(1).Search("abc", Arg.Any<CancellationToken>()));
        _client.DidNotReceive().Search("ab", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Search_ClearingTheQuery_ResetsResults()
    {
        _client.Search("muse", Arg.Any<CancellationToken>()).Returns(new SearchResultsDto { Artists = [new ArtistOnlyDto { Id = 1, Name = "Muse" }] });
        var cut = Render<Search>();
        cut.Find(".search-input").Input("muse");
        cut.WaitForAssertion(() => cut.Find(".artist-link-text"));

        cut.Find(".search-input").Input("");

        cut.Find(".search-hint").TextContent.ShouldBe("Start typing to search your library.");
    }

    [Fact]
    public void Search_DisposedMidSearch_DoesNotRenderStaleResults()
    {
        var pending = new TaskCompletionSource<SearchResultsDto>();
        _client.Search("late", Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = Render<Search>();
        cut.Find(".search-input").Input("late");
        SpinWait.SpinUntil(() => _client.ReceivedCalls().Any(), TimeSpan.FromSeconds(5)).ShouldBeTrue();

        cut.Instance.Dispose();

        Should.NotThrow(() => pending.SetResult(new SearchResultsDto()));
    }

    // ---- tracks ----

    private void GivenLibrary(params TrackDto[] tracks)
    {
        _client.GetTracksPage(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TrackDto> { Items = tracks, TotalCount = tracks.Length });
    }

    [Fact]
    public void Tracks_ShowsTheTotalAndRows()
    {
        GivenLibrary(Track(1, "One"), Track(2, "Two"));

        var cut = Render<Tracks>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("h3").TextContent.ShouldBe("Tracks (2)");
            cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(2);
        });
        cut.Find(".track-table-row .artists").TextContent.ShouldContain("Artist");
    }

    [Fact]
    public void Tracks_ShowsRecordedDateWhenKnown()
    {
        var dated = Track(1);
        GivenLibrary(new TrackDto
        {
            Id = 1,
            Title = "Dated",
            Artists = [],
            Album = "A",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2001,
            RecordedDate = new DateOnly(2001, 2, 3),
            Duration = dated.Duration
        });

        var cut = Render<Tracks>();

        cut.WaitForAssertion(() => cut.Find(".cell.year").TextContent.ShouldBe("2001-02-03"));
    }

    [Fact]
    public void Tracks_ClickingAHeader_SortsAndClickingAgainReverses()
    {
        GivenLibrary(Track(1));
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(1));

        cut.Find(".sortable.year").Click();
        cut.Find(".sortable.year .sort-indicator").TextContent.ShouldBe("▲");
        cut.Find(".sortable.year").Click();
        cut.Find(".sortable.year .sort-indicator").TextContent.ShouldBe("▼");
        cut.Find(".sortable.title .sort-indicator").TextContent.ShouldBeEmpty();

        cut.WaitForAssertion(() => _client.Received().GetTracksPage(
            Arg.Is<TrackQuery>(q => q!.Sort == TrackSortField.Year && q.Descending), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Tracks_Searching_RefreshesWithTheSearchText()
    {
        GivenLibrary(Track(1));
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(1));

        cut.Find(".search-input").Input("abc");
        cut.Find(".search-input").Input("abcd");

        cut.WaitForAssertion(() => _client.Received().GetTracksPage(
            Arg.Is<TrackQuery>(q => q!.Search == "abcd"), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Tracks_ClickingARow_QueuesFromThatTrackOn()
    {
        GivenLibrary(Track(1), Track(2), Track(3));
        _client.GetTrackIds(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns([1u, 2u, 3u]);
        _client.GetTracksByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns(ci => [.. ci.Arg<IReadOnlyCollection<uint>>()!.Select(id => Track(id))]);
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(3));

        cut.FindAll(".track-table-row")[1].Click();

        cut.WaitForAssertion(() => _playlist.Count.ShouldBe(2));
        _playlist.CurrentValueId.ShouldBe(2ul);
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row.active").ShouldHaveSingleItem());
    }

    [Fact]
    public void Tracks_ClickingATrackMissingFromTheIdList_QueuesFromTheStart()
    {
        GivenLibrary(Track(9));
        _client.GetTrackIds(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns([1u, 2u]);
        _client.GetTracksByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns(ci => [.. ci.Arg<IReadOnlyCollection<uint>>()!.Select(id => Track(id))]);
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(1));

        cut.Find(".track-table-row").Click();

        cut.WaitForAssertion(() => _playlist.CurrentValueId.ShouldBe(1ul));
    }

    [Fact]
    public void Tracks_ClickingARowWhoseTracksVanished_DoesNothing()
    {
        GivenLibrary(Track(1));
        _client.GetTrackIds(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns([1u]);
        _client.GetTracksByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>()).Returns([]);
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(1));

        cut.Find(".track-table-row").Click();

        _playlist.Count.ShouldBe(0);
    }

    [Fact]
    public void Tracks_HugeResult_CapsTheQueueAndSaysSo()
    {
        GivenLibrary(Track(1));
        _client.GetTrackIds(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns([.. Enumerable.Range(1, 1500).Select(i => (uint)i)]);
        _client.GetTracksByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns(ci => [.. ci.Arg<IReadOnlyCollection<uint>>()!.Select(id => Track(id))]);
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(1));

        cut.Find(".track-table-row").Click();

        cut.WaitForAssertion(() => cut.Find(".queue-note").TextContent.ShouldContain("1000"));
        _playlist.Count.ShouldBe(1000);
    }

    [Fact]
    public async Task Tracks_PlaylistChangesElsewhere_UpdateTheActiveRowAndUnsubscribeOnDispose()
    {
        GivenLibrary(Track(1), Track(2));
        var cut = Render<Tracks>();
        cut.WaitForAssertion(() => cut.FindAll(".track-table-row:not(.placeholder)").Count.ShouldBe(2));

        _playlist.SetPlayList([Track(2)]);
        await cut.InvokeAsync(() => _playlist.Play(0));

        cut.WaitForAssertion(() => cut.Find(".track-table-row.active").TextContent.ShouldContain("Song"));

        cut.Instance.Dispose();
        await cut.InvokeAsync(() => _playlist.Play(0));
    }
}
