using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components;
using Plaat.Frontend.Components.Pages;
using Plaat.Frontend.Components.Playlists;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The playlist overview (create, delete, pin, smart playlists) and a single playlist's page
/// (play, edit order, duplicate, edit smart rules).
/// </summary>
public sealed class PlaylistPagesComponentTests : BunitContext, IAsyncLifetime
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly MusicPlaylist _queue = new();

    public PlaylistPagesComponentTests()
    {
        Services.AddSingleton(_client);
        Services.AddSingleton(_queue);
        Services.AddSingleton<NavPlaylistsState>();
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<MusicFileList>();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }

    private static TrackDto Track(uint id)
    {
        return new TrackDto
        {
            Id = id,
            Title = $"T{id}",
            Artists = [],
            Album = "A",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = id,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    private static PlaylistDetailsDto Details(string name, bool isSmart = false, params uint[] trackIds)
    {
        IReadOnlyCollection<TrackDto> tracks = [.. trackIds.Select(Track)];

        return isSmart
            ? new SmartPlaylistDetailsDto { Id = 5, PlaylistName = name, IsSmart = true, IsPinned = false, TrackCount = tracks.Count, Tracks = tracks }
            : new PlaylistDetailsDto { Id = 5, PlaylistName = name, IsSmart = false, IsPinned = false, TrackCount = tracks.Count, Tracks = tracks };
    }

    private (IRenderedComponent<IComponent> Root, IRenderedComponent<Playlist> Page) RenderPlaylist(PlaylistDetailsDto? details, string name = "Mix")
    {
        _client.GetPlaylist(name, Arg.Any<CancellationToken>()).Returns(details);

        var root = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<Playlist>(1);
            builder.AddAttribute(2, nameof(Playlist.PlaylistName), name);
            builder.CloseComponent();
        });

        return (root, root.FindComponent<Playlist>());
    }

    private static Stub<MusicFileList> TrackList(IRenderedComponent<Playlist> page)
    {
        return page.FindComponent<Stub<MusicFileList>>().Instance;
    }

    // ---- single playlist ----

    [Fact]
    public void Playlist_Unknown_ShowsNotFound()
    {
        var (_, page) = RenderPlaylist(null);

        page.Find("h3").TextContent.ShouldBe("Playlist not found");
    }

    [Fact]
    public void Playlist_Regular_ShowsTracksAndAnEditButton()
    {
        var (_, page) = RenderPlaylist(Details("Mix", false, 1, 2));

        page.Find(".playlist-title").TextContent.ShouldBe("Mix");
        page.FindAll("h3").Single(h => h.TextContent.StartsWith("Tracks")).TextContent.ShouldBe("Tracks (2)");
        page.FindAll(".action-btn").Select(b => b.TextContent.Trim()).ShouldContain("Edit");
        page.FindAll(".action-btn").Select(b => b.TextContent.Trim()).ShouldNotContain("Edit Rules");
    }

    [Fact]
    public void Playlist_PlayAndShuffle_QueueItsTracks()
    {
        var (_, page) = RenderPlaylist(Details("Mix", false, 1, 2, 3));

        page.FindAll(".action-btn")[0].Click();
        _queue.IsShuffled.ShouldBeFalse();
        _queue.Count.ShouldBe(3);
        _queue.CurrentValueId.ShouldBe(1ul);

        page.FindAll(".action-btn")[1].Click();
        _queue.IsShuffled.ShouldBeTrue();
        _queue.Count.ShouldBe(3);
    }

    [Fact]
    public void Playlist_Empty_PlayAndShuffleDoNothing()
    {
        var (_, page) = RenderPlaylist(Details("Mix"));

        page.FindAll(".action-btn")[0].Click();
        page.FindAll(".action-btn")[1].Click();

        _queue.Count.ShouldBe(0);
    }

    [Fact]
    public void Playlist_EditMode_PassesEditControlsToTheTrackList()
    {
        var (_, page) = RenderPlaylist(Details("Mix", false, 1));

        page.FindAll(".action-btn")[2].Click();

        TrackList(page).Parameters.Get(x => x.ShowEditControls).ShouldBeTrue();
        page.FindAll(".action-btn")[2].ClassList.ShouldContain("active");
    }

    [Fact]
    public async Task Playlist_RemovingATrack_RemovesItRemotelyAndLocally()
    {
        var (_, page) = RenderPlaylist(Details("Mix", false, 1, 2));
        var onRemove = TrackList(page).Parameters.Get(x => x.OnRemoveTrack);

        await page.InvokeAsync(() => onRemove.InvokeAsync(0));
        await page.InvokeAsync(() => onRemove.InvokeAsync(5));

        await _client.Received(1).RemoveFromPlaylist(5, 1, Arg.Any<CancellationToken>());
        page.FindAll("h3").Single(h => h.TextContent.StartsWith("Tracks")).TextContent.ShouldBe("Tracks (1)");
    }

    [Fact]
    public async Task Playlist_MovingATrack_ReordersAndSavesTheNewOrder()
    {
        var (_, page) = RenderPlaylist(Details("Mix", false, 1, 2, 3));
        var onMove = TrackList(page).Parameters.Get(x => x.OnMoveTrack);

        await page.InvokeAsync(() => onMove.InvokeAsync((0, 2)));
        await page.InvokeAsync(() => onMove.InvokeAsync((1, 1)));
        await page.InvokeAsync(() => onMove.InvokeAsync((-1, 1)));
        await page.InvokeAsync(() => onMove.InvokeAsync((0, 9)));

        await _client.Received(1).ReorderPlaylist(
            5, Arg.Is<IReadOnlyList<uint>>(ids => ids!.SequenceEqual(new uint[] { 2, 3, 1 })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playlist_Smart_IgnoresEditsToItsTrackList()
    {
        var (_, page) = RenderPlaylist(Details("Smart", true, 1, 2), "Smart");

        await page.InvokeAsync(() => TrackList(page).Parameters.Get(x => x.OnRemoveTrack).InvokeAsync(0));
        await page.InvokeAsync(() => TrackList(page).Parameters.Get(x => x.OnMoveTrack).InvokeAsync((0, 1)));

        _client.ReceivedCalls().ShouldNotContain(c => c.GetMethodInfo().Name == nameof(IPlaatClient.RemoveFromPlaylist));
        _client.ReceivedCalls().ShouldNotContain(c => c.GetMethodInfo().Name == nameof(IPlaatClient.ReorderPlaylist));
    }

    private static void OpenDuplicateModal(IRenderedComponent<IComponent> root)
    {
        root.Find(".mud-menu button").Click();
        root.WaitForElement(".mud-menu-item").Click();
        root.WaitForElement(".modal-input");
    }

    [Fact]
    public void Duplicate_PrefillsACopyNameAndNavigatesToTheCopy()
    {
        _client.DuplicatePlaylist(5, false, "Mix (copy)", Arg.Any<CancellationToken>()).Returns(true);
        var (root, page) = RenderPlaylist(Details("Mix", false, 1));

        OpenDuplicateModal(root);
        page.Find(".modal-input").GetAttribute("value").ShouldBe("Mix (copy)");
        page.Find(".modal-input").KeyDown("Enter");

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/playlists/Mix%20%28copy%29");
        page.FindAll(".modal").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("  ", false, "Playlist name cannot be empty.")]
    [InlineData("Taken", false, "A playlist with this name already exists.")]
    [InlineData("Boom", true, "Failed to duplicate playlist.")]
    public void Duplicate_ProblemsAreShownInTheModal(string name, bool throws, string error)
    {
        if (throws)
        {
            _client.DuplicatePlaylist(5, false, name, Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        }

        var (root, page) = RenderPlaylist(Details("Mix", false, 1));

        OpenDuplicateModal(root);
        page.Find(".modal-input").Input(name);
        page.Find(".modal-input").KeyDown("a");
        page.Find(".modal-btn-ok").Click();

        page.Find(".modal-error").TextContent.ShouldBe(error);
        page.Find(".modal-btn-cancel").Click();
        page.FindAll(".modal").ShouldBeEmpty();
    }

    private void GivenRules(int? limit)
    {
        _client.GetSmartPlaylistRules(5, Arg.Any<CancellationToken>()).Returns(new SmartPlaylistRulesDto
        {
            RootGroup = new SmartPlaylistRuleGroupDto { Rules = [new YearRuleDto { RuleType = PlayCountPlaylistRuleType.HigherThan, Year = 1990 }] },
            Limit = limit,
            LimitOrderBy = limit is null ? null : SmartPlaylistLimitOrderByDto.RecentlyAdded
        });
    }

    [Fact]
    public void EditRules_LoadsTheRulesAndSavesChanges()
    {
        GivenRules(limit: 10);
        _client.UpdateSmartPlaylist(5, Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var (_, page) = RenderPlaylist(Details("Smart", true, 1), "Smart");

        page.FindAll(".action-btn").Single(b => b.TextContent.Contains("Edit Rules")).Click();
        page.Find("input[type=checkbox]").HasAttribute("checked").ShouldBeTrue();
        page.Find(".limit-section .rule-input-number").Change("15");
        page.Find(".limit-section .rule-input-number").Change("0");
        page.Find(".modal-btn-ok").Click();

        _client.Received(1).UpdateSmartPlaylist(
            5,
            Arg.Is<CreateSmartPlaylistRequest>(r => r!.Limit == 15 && r.LimitOrderBy == SmartPlaylistLimitOrderByDto.RecentlyAdded
                && r.RootGroup.Rules.Single() is YearRuleDto),
            Arg.Any<CancellationToken>());
        _client.Received(2).GetPlaylist("Smart", Arg.Any<CancellationToken>());
        page.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void EditRules_WithoutALimit_SavesUnlimited()
    {
        GivenRules(limit: null);
        _client.UpdateSmartPlaylist(5, Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var (_, page) = RenderPlaylist(Details("Smart", true), "Smart");

        page.FindAll(".action-btn").Single(b => b.TextContent.Contains("Edit Rules")).Click();
        page.Find(".modal-btn-ok").Click();

        _client.Received(1).UpdateSmartPlaylist(
            5, Arg.Is<CreateSmartPlaylistRequest>(r => r!.Limit == null && r.LimitOrderBy == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditRules_RulesGone_DoesNotOpen()
    {
        _client.GetSmartPlaylistRules(5, Arg.Any<CancellationToken>()).Returns((SmartPlaylistRulesDto?)null);
        var (_, page) = RenderPlaylist(Details("Smart", true), "Smart");

        page.FindAll(".action-btn").Single(b => b.TextContent.Contains("Edit Rules")).Click();

        page.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void EditRules_InvalidOrFailedSaves_ShowAnError()
    {
        GivenRules(limit: null);
        var (_, page) = RenderPlaylist(Details("Smart", true), "Smart");
        page.FindAll(".action-btn").Single(b => b.TextContent.Contains("Edit Rules")).Click();
        var editor = page.FindComponent<SmartRuleGroupEditor>().Instance;

        page.Find(".rule-row .remove-rule-btn").Click();
        page.Find(".modal-btn-ok").Click();
        page.Find(".modal-error").TextContent.ShouldBe("Add at least one rule.");

        editor.Group.Rules.Add(new SmartRuleUiModel { Type = SmartRuleType.DurationRange });
        page.Find(".modal-btn-ok").Click();
        page.Find(".modal-error").TextContent.ShouldBe("Duration range rules need at least a minimum or maximum.");

        editor.Group.Rules[0].MaxDuration = TimeSpan.FromMinutes(5);
        _client.UpdateSmartPlaylist(5, Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        page.Find(".modal-btn-ok").Click();
        page.Find(".modal-error").TextContent.ShouldBe("Failed to update playlist.");

        _client.UpdateSmartPlaylist(5, Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException()));
        page.Find(".modal-btn-ok").Click();
        page.Find(".modal-error").TextContent.ShouldBe("Failed to update playlist.");

        page.Find(".modal-btn-cancel").Click();
        page.FindAll(".modal").ShouldBeEmpty();
    }

    // ---- playlists overview ----

    private void GivenPlaylists(params PlaylistOnlyDto[] playlists)
    {
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns(playlists);
    }

    private static PlaylistOnlyDto Summary(uint id, string name, bool isSmart = false, bool isPinned = false)
    {
        return new PlaylistOnlyDto { Id = id, PlaylistName = name, IsSmart = isSmart, TrackCount = (int)id, IsPinned = isPinned };
    }

    [Fact]
    public void Playlists_AreSortedAndFilterable()
    {
        GivenPlaylists(Summary(1, "zeta"), Summary(2, "Alpha", isPinned: true), Summary(3, "beta/mix"));

        var cut = Render<Playlists>();

        cut.FindAll(".album-link-text").Select(e => e.TextContent).ShouldBe(["Alpha (2)", "beta/mix (3)", "zeta (1)"]);
        cut.FindAll(".album-link")[1].GetAttribute("href").ShouldBe("Playlists/beta%2Fmix");
        cut.FindAll(".pin-btn")[0].ClassList.ShouldContain("active");

        cut.Find(".search-input").Input("ET");

        cut.FindAll(".album-link-text").Select(e => e.TextContent).ShouldBe(["beta/mix (3)", "zeta (1)"]);
    }

    [Fact]
    public void Playlists_TogglingAPin_SavesAndRefreshes()
    {
        GivenPlaylists(Summary(1, "One", isSmart: true, isPinned: false));
        var cut = Render<Playlists>();

        cut.Find(".pin-btn").Click();

        _client.Received(1).SetPlaylistPinned(1, true, true, Arg.Any<CancellationToken>());
        _client.Received(2).GetPlaylists(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Playlists_Delete_ConfirmsThenDeletesAndRefreshes()
    {
        GivenPlaylists(Summary(1, "One", isSmart: true));
        var cut = Render<Playlists>();

        cut.Find(".delete-btn").Click();
        cut.Find(".modal-message").TextContent.ShouldContain("\"One\"");
        cut.Find(".modal-btn-danger").Click();

        _client.Received(1).DeletePlaylist(1, true, Arg.Any<CancellationToken>());
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void Playlists_DeleteFailure_KeepsTheModalOpenWithAnError()
    {
        GivenPlaylists(Summary(1, "One"));
        _client.DeletePlaylist(1, false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        var cut = Render<Playlists>();

        cut.Find(".delete-btn").Click();
        cut.Find(".modal-btn-danger").Click();

        cut.Find(".modal-error").TextContent.ShouldBe("Failed to delete playlist.");
        cut.Find(".modal-overlay").Click();
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("  ", null, "Playlist name cannot be empty.")]
    [InlineData("Taken", false, "A playlist with this name already exists.")]
    [InlineData("Boom", null, "Failed to create playlist.")]
    public void Playlists_CreateProblems_AreShownInTheModal(string name, bool? created, string error)
    {
        if (created is { } result)
        {
            _client.CreatePlaylist(name, Arg.Any<CancellationToken>()).Returns(result);
        }
        else
        {
            _client.CreatePlaylist(name, Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        }

        var cut = Render<Playlists>();

        cut.Find(".toolbar-btn").Click();
        cut.Find(".modal-input").Input(name);
        cut.Find(".modal-btn-ok").Click();

        cut.Find(".modal-error").TextContent.ShouldBe(error);
    }

    [Fact]
    public void Playlists_Create_WithEnterCreatesTrimmedNameAndCloses()
    {
        _client.CreatePlaylist("New", Arg.Any<CancellationToken>()).Returns(true);
        var cut = Render<Playlists>();

        cut.Find(".toolbar-btn").Click();
        cut.Find(".modal-input").Input(" New ");
        cut.Find(".modal-input").KeyDown("x");
        cut.Find(".modal-input").KeyDown("Enter");

        _client.Received(1).CreatePlaylist("New", Arg.Any<CancellationToken>());
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void Playlists_CreateSmart_SendsRulesAndOptionalLimit()
    {
        _client.CreateSmartPlaylist("Recent", Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var cut = Render<Playlists>();

        cut.Find(".toolbar-btn-smart").Click();
        cut.Find(".modal-input").Input("Recent");
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find(".limit-section .rule-input-number").Change("50");
        cut.Find(".limit-section .rule-input-number").Change("abc");
        cut.Find(".modal-btn-ok").Click();

        _client.Received(1).CreateSmartPlaylist(
            "Recent",
            Arg.Is<CreateSmartPlaylistRequest>(r => r!.Limit == 50 && r.RootGroup.Rules.Single() is PlayCountPlaylistRuleDto),
            Arg.Any<CancellationToken>());
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public void Playlists_CreateSmartWithoutLimit_SendsNoLimit()
    {
        _client.CreateSmartPlaylist("All", Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var cut = Render<Playlists>();

        cut.Find(".toolbar-btn-smart").Click();
        cut.Find(".modal-input").Input("All");
        cut.Find(".modal-btn-ok").Click();

        _client.Received(1).CreateSmartPlaylist(
            "All", Arg.Is<CreateSmartPlaylistRequest>(r => r!.Limit == null && r.LimitOrderBy == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Playlists_CreateSmartProblems_AreShownInTheModal()
    {
        var cut = Render<Playlists>();
        cut.Find(".toolbar-btn-smart").Click();

        cut.Find(".modal-btn-ok").Click();
        cut.Find(".modal-error").TextContent.ShouldBe("Playlist name cannot be empty.");

        cut.Find(".modal-input").Input("Smart");
        var editor = cut.FindComponent<SmartRuleGroupEditor>().Instance;
        editor.Group.Rules.Clear();
        cut.Find(".modal-btn-ok").Click();
        cut.Find(".modal-error").TextContent.ShouldBe("Add at least one rule.");

        editor.Group.Rules.Add(new SmartRuleUiModel { Type = SmartRuleType.DurationRange });
        cut.Find(".modal-btn-ok").Click();
        cut.Find(".modal-error").TextContent.ShouldBe("Duration range rules need at least a minimum or maximum.");

        editor.Group.Rules[0].MinDuration = TimeSpan.FromMinutes(1);
        _client.CreateSmartPlaylist("Smart", Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        cut.Find(".modal-btn-ok").Click();
        cut.Find(".modal-error").TextContent.ShouldBe("A playlist with this name already exists.");

        _client.CreateSmartPlaylist("Smart", Arg.Any<CreateSmartPlaylistRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException()));
        cut.Find(".modal-btn-ok").Click();
        cut.Find(".modal-error").TextContent.ShouldBe("Failed to create playlist.");

        cut.Find(".modal-btn-cancel").Click();
        cut.FindAll(".modal").ShouldBeEmpty();
    }

    [Fact]
    public async Task Playlists_ChangesFromElsewhereRerenderUntilDisposed()
    {
        GivenPlaylists(Summary(1, "Old"));
        var cut = Render<Playlists>();
        var state = Services.GetRequiredService<NavPlaylistsState>();

        GivenPlaylists(Summary(1, "Old"), Summary(2, "New"));
        await cut.InvokeAsync(() => state.RefreshAsync());

        cut.WaitForAssertion(() => cut.FindAll(".album-link-text").Count.ShouldBe(2));

        cut.Instance.Dispose();
    }
}
