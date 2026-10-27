using Bunit;
using Microsoft.AspNetCore.Components;
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
/// The shared track table: sorting and searching, the "overwrite or add to queue?" prompt, queue and
/// playlist edit controls (remove, drag to reorder, drag onto a sidebar playlist) and the per-row menu.
/// </summary>
public sealed class MusicFileListInteractionTests : BunitContext, IAsyncLifetime
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly MusicPlaylist _queue = [];

    public MusicFileListInteractionTests()
    {
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns([
            new PlaylistOnlyDto { Id = 7, PlaylistName = "Regular List", IsSmart = false, TrackCount = 0, IsPinned = false },
            new PlaylistOnlyDto { Id = 8, PlaylistName = "Smart List", IsSmart = true, TrackCount = 0, IsPinned = false }
        ]);
        Services.AddSingleton(_client);
        Services.AddSingleton(_queue);
        Services.AddSingleton<NavPlaylistsState>();
        Services.AddSingleton<TrackRatingState>();
        Services.AddMudServices();
        Services.AddSingleton(_snackbar);
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<TrackDetailsModal>();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }

    private static TrackDto Track(uint id, string title, string artist, string album, uint year, uint plays)
    {
        return new TrackDto
        {
            Id = id,
            Title = title,
            Artists = [new ArtistOnlyDto { Id = id, Name = artist }],
            Album = album,
            AlbumId = id,
            PlayCount = plays,
            TrackNumber = id,
            DiscNumber = 1,
            Year = year,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    private static readonly TrackDto[] Tracks =
    [
        Track(1, "Bravo", "Zed", "Middle", 2000, 5),
        Track(2, "alpha", "Amy", "Zulu", 1990, 9),
        Track(3, "Charlie", "Mo", "Alpha", 2010, 1)
    ];

    private IRenderedComponent<IComponent> RenderList()
    {
        return Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MusicFileList>(1);
            builder.AddAttribute(2, nameof(MusicFileList.Music), (IReadOnlyCollection<TrackDto>)Tracks);
            builder.CloseComponent();
        });
    }

    private IRenderedComponent<MusicFileList> RenderTable(Action<ComponentParameterCollectionBuilder<MusicFileList>>? parameters = null)
    {
        return Render<MusicFileList>(p =>
        {
            p.Add(x => x.Music, Tracks);
            parameters?.Invoke(p);
        });
    }

    private static IReadOnlyList<string> Titles(IRenderedComponent<MusicFileList> cut)
    {
        return [.. cut.FindAll(".track-title").Select(t => t.TextContent)];
    }

    // ---- sorting / searching ----

    [Theory]
    [InlineData(".sortable.title", new[] { "alpha", "Bravo", "Charlie" })]
    [InlineData(".sortable.artists", new[] { "alpha", "Charlie", "Bravo" })]
    [InlineData(".sortable.album", new[] { "Charlie", "Bravo", "alpha" })]
    [InlineData(".sortable.year", new[] { "alpha", "Bravo", "Charlie" })]
    [InlineData(".right.sortable", new[] { "Charlie", "Bravo", "alpha" })]
    public void ClickingAColumn_SortsAscendingThenDescending(string header, string[] ascending)
    {
        var cut = RenderTable();
        Titles(cut).ShouldBe(["Bravo", "alpha", "Charlie"]);

        cut.Find(header).Click();
        Titles(cut).ShouldBe(ascending);
        cut.Find($"{header} .sort-indicator").TextContent.ShouldBe("▲");

        cut.Find(header).Click();
        Titles(cut).ShouldBe(ascending.Reverse());
        cut.Find($"{header} .sort-indicator").TextContent.ShouldBe("▼");
    }

    [Fact]
    public void WithEditControls_ColumnsCannotBeSorted()
    {
        var cut = RenderTable(p => p.Add(x => x.ShowEditControls, true));

        cut.Find(".sortable.title").Click();

        Titles(cut).ShouldBe(["Bravo", "alpha", "Charlie"]);
        cut.FindAll(".remove-button").Count.ShouldBe(3);
        cut.FindAll(".drag-handle").Count.ShouldBe(3);
    }

    [Fact]
    public void Search_FiltersAndRanksByTitleAndArtist()
    {
        var cut = RenderTable();

        cut.Find(".search-input").Input("mo");

        Titles(cut).ShouldBe(["Charlie"]);

        cut.Find(".search-input").Input("  ");
        Titles(cut).Count.ShouldBe(3);
    }

    // ---- playing ----

    [Fact]
    public void ClickingTheTrackAlreadyPlaying_DoesNothing()
    {
        var cut = RenderTable();
        cut.FindAll(".track-table-row")[0].Click();
        _queue.Add(Track(99, "extra", "x", "y", 1, 1));

        cut.FindAll(".track-table-row")[0].Click();

        _queue.Count.ShouldBe(4);
        cut.FindAll(".modal").ShouldBeEmpty();
        cut.Find(".track-table-row.active .now-playing-icon").ClassList.ShouldContain("icon-play");
    }

    [Fact]
    public void WhileSomethingPlays_ClickingAnotherTrackAsksAndCanOverwriteTheQueue()
    {
        var cut = RenderTable();
        cut.FindAll(".track-table-row")[0].Click();

        cut.FindAll(".track-table-row")[1].Click();
        cut.Find(".modal-title").TextContent.ShouldBe("Overwrite playlist?");
        cut.FindAll(".modal-footer button")[0].Click();

        _queue.CurrentValueId.ShouldBe(2ul);
        cut.FindAll(".modal-title").ShouldBeEmpty();
    }

    [Fact]
    public void WhileSomethingPlays_TheTrackCanBeAddedToTheQueueInstead()
    {
        var cut = RenderTable();
        cut.FindAll(".track-table-row")[0].Click();

        cut.FindAll(".track-table-row")[1].Click();
        cut.FindAll(".modal-footer button")[1].Click();

        _queue.Count.ShouldBe(4);
        _queue.Last().Id.ShouldBe(2u);
        _queue.CurrentValueId.ShouldBe(1ul);
    }

    [Fact]
    public void WhileSomethingPlays_ThePromptCanBeCancelled()
    {
        var cut = RenderTable();
        cut.FindAll(".track-table-row")[0].Click();

        cut.FindAll(".track-table-row")[1].Click();
        cut.Find(".modal-footer .btn-secondary").Click();

        _queue.CurrentValueId.ShouldBe(1ul);
        cut.FindAll(".modal-title").ShouldBeEmpty();
    }

    [Fact]
    public void WithoutConfirmation_ClickingOverwritesImmediately()
    {
        var cut = RenderTable(p => p.Add(x => x.AskConfirmation, false));
        cut.FindAll(".track-table-row")[0].Click();

        cut.FindAll(".track-table-row")[2].Click();

        _queue.CurrentValueId.ShouldBe(3ul);
        cut.FindAll(".modal-title").ShouldBeEmpty();
    }

    [Fact]
    public void QueueMusicWithoutTheClickedTrack_DoesNotChangeTheQueue()
    {
        var cut = RenderTable(p => p.Add(x => x.QueueMusic, new[] { Track(50, "other", "o", "o", 1, 1) }));

        cut.FindAll(".track-table-row")[0].Click();

        _queue.Count.ShouldBe(0);
    }

    // ---- queue & edit controls ----

    private IRenderedComponent<MusicFileList> RenderQueue()
    {
        _queue.SetPlayList(Tracks);

        return Render<MusicFileList>(p => p
            .Add(x => x.Music, (IReadOnlyCollection<TrackDto>)_queue.ToList())
            .Add(x => x.ShowQueueControls, true));
    }

    [Fact]
    public void QueueControls_RemoveTakesTheTrackOutOfTheQueue()
    {
        var cut = RenderQueue();

        cut.FindAll(".remove-button")[0].GetAttribute("title").ShouldBe("Remove from queue");
        cut.FindAll(".remove-button")[0].Click();

        _queue.Select(t => t.Id).ShouldBe([2u, 3u]);
    }

    [Fact]
    public void QueueControls_DraggingReordersTheQueue()
    {
        var cut = RenderQueue();

        cut.FindAll(".track-table-row")[0].DragStart();
        cut.FindAll(".track-table-row")[0].ClassList.ShouldContain("dragging");
        cut.FindAll(".track-table-row")[2].DragOver();
        cut.FindAll(".track-table-row")[2].ClassList.ShouldContain("drop-after");
        cut.FindAll(".track-table-row")[2].Drop();

        _queue.Select(t => t.Id).ShouldBe([2u, 3u, 1u]);
    }

    [Fact]
    public void EditControls_ReportRemovalsAndMovesToThePage()
    {
        var removed = new List<int>();
        var moved = new List<(int, int)>();
        var cut = RenderTable(p => p
            .Add(x => x.ShowEditControls, true)
            .Add(x => x.OnRemoveTrack, i => removed.Add(i))
            .Add(x => x.OnMoveTrack, m => moved.Add(m)));

        cut.FindAll(".remove-button")[1].Click();
        cut.FindAll(".remove-button")[1].GetAttribute("title").ShouldBe("Remove from playlist");

        cut.FindAll(".track-table-row")[2].DragStart();
        cut.FindAll(".track-table-row")[2].DragOver();
        cut.FindAll(".track-table-row")[0].DragOver();
        cut.FindAll(".track-table-row")[0].ClassList.ShouldContain("drop-before");
        cut.FindAll(".track-table-row")[1].ClassList.ShouldNotContain("drop-before");
        cut.FindAll(".track-table-row")[0].Drop();

        removed.ShouldBe([1]);
        moved.ShouldBe([(2, 0)]);
    }

    [Fact]
    public void Drag_DroppingOnItselfOrEnding_ChangesNothing()
    {
        var moved = new List<(int, int)>();
        var cut = RenderTable(p => p
            .Add(x => x.ShowEditControls, true)
            .Add(x => x.OnMoveTrack, m => moved.Add(m)));

        cut.FindAll(".track-table-row")[1].DragStart();
        cut.FindAll(".track-table-row")[1].Drop();
        cut.FindAll(".track-table-row")[1].DragStart();
        cut.FindAll(".track-table-row")[1].DragEnd();
        cut.FindAll(".track-table-row")[0].DragOver();
        cut.FindAll(".track-table-row")[0].Drop();

        moved.ShouldBeEmpty();
        cut.FindAll(".track-table-row").ShouldAllBe(r => !r.ClassList.Contains("dragging"));
    }

    [Fact]
    public void Drag_WithoutControls_OnlyOffersTheTrackToSidebarPlaylists()
    {
        var cut = RenderTable();
        var nav = Services.GetRequiredService<NavPlaylistsState>();

        cut.FindAll(".track-table-row")[0].DragStart();
        nav.DraggedTrack!.Id.ShouldBe(1u);
        cut.FindAll(".track-table-row")[1].DragOver();
        cut.FindAll(".track-table-row")[1].ClassList.ShouldNotContain("drop-after");
        cut.FindAll(".track-table-row")[0].DragEnd();

        nav.DraggedTrack.ShouldBeNull();
    }

    [Fact]
    public void RemoveButton_WithNeitherControlMode_IsNotShown()
    {
        RenderTable().FindAll(".remove-button").ShouldBeEmpty();
    }

    // ---- row menu ----

    private static void ClickMenuItem(IRenderedComponent<IComponent> root, string label)
    {
        root.FindAll(".track-menu .mud-menu button")[0].Click();
        root.WaitForElements(".mud-menu-item").First(i => i.TextContent.Contains(label)).Click();
    }

    [Fact]
    public void Menu_AddToQueueAndPlayNext()
    {
        _queue.SetPlayList([Track(40, "Now", "n", "n", 1, 1), Track(41, "Later", "l", "l", 1, 1)]);
        _queue.Play(0);
        var root = RenderList();

        ClickMenuItem(root, "Add to queue");
        _queue.Last().Id.ShouldBe(1u);

        ClickMenuItem(root, "Play next");
        _queue[1].Id.ShouldBe(1u);
    }

    [Fact]
    public void Menu_TrackInfo_OpensTheModalForThatTrackAndClosingClearsIt()
    {
        var root = RenderList();

        ClickMenuItem(root, "Track info");

        var modal = root.FindComponent<Bunit.TestDoubles.Stub<TrackDetailsModal>>();
        modal.Instance.Parameters.Get(x => x.TrackId).ShouldBe(1u);
        root.InvokeAsync(() => modal.Instance.Parameters.Get(x => x.OnClose).InvokeAsync());
        root.FindComponent<Bunit.TestDoubles.Stub<TrackDetailsModal>>().Instance.Parameters.Get(x => x.TrackId).ShouldBeNull();
    }

    [Theory]
    [InlineData(true, "Metadata reloaded from file", Severity.Success)]
    [InlineData(false, "Track file could not be found on disk", Severity.Warning)]
    public void Menu_ReloadMetadata_ReportsTheOutcome(bool success, string message, Severity severity)
    {
        _client.ReloadTrackMetadata(1, Arg.Any<CancellationToken>()).Returns(success);
        var root = RenderList();

        ClickMenuItem(root, "Force reload metadata");

        root.WaitForAssertion(() => _snackbar.Received(1).Add(message, severity));
    }

    [Fact]
    public void Menu_ReloadMetadataFailure_ReportsAnError()
    {
        _client.ReloadTrackMetadata(1, Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException()));
        var root = RenderList();

        ClickMenuItem(root, "Force reload metadata");

        root.WaitForAssertion(() => _snackbar.Received(1).Add("Failed to reload metadata", Severity.Error));
    }

    [Fact]
    public void Menu_AddToPlaylist_OnlyOffersRegularPlaylistsAndAddsTheTrack()
    {
        var root = RenderList();

        root.FindAll(".track-menu .mud-menu button")[0].Click();
        root.WaitForElements(".mud-menu-item").First(i => i.TextContent.Contains("Add to playlist")).Click();

        var items = root.WaitForElements(".mud-menu-item");
        items.ShouldNotContain(i => i.TextContent.Contains("Smart List"));
        items.First(i => i.TextContent.Contains("Regular List")).Click();

        root.WaitForAssertion(() => _client.Received(1).AddToPlaylist(7, 1, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Menu_AddToPlaylistFailure_IsSwallowed()
    {
        _client.AddToPlaylist(7, 1, Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        var root = RenderList();

        root.FindAll(".track-menu .mud-menu button")[0].Click();
        root.WaitForElements(".mud-menu-item").First(i => i.TextContent.Contains("Add to playlist")).Click();
        root.WaitForElements(".mud-menu-item").First(i => i.TextContent.Contains("Regular List")).Click();

        root.WaitForAssertion(() => _client.Received(1).AddToPlaylist(7, 1, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task QueueChangesElsewhere_UpdateThePlayingRowUntilDisposed()
    {
        var cut = RenderTable();

        _queue.SetPlayList(Tracks);
        await cut.InvokeAsync(() => _queue.Play(2));

        cut.WaitForAssertion(() => cut.Find(".track-table-row.active .track-title").TextContent.ShouldBe("Charlie"));
        _queue.IsPlaying = false;
        await cut.InvokeAsync(() => _queue.Play(2));
        _queue.IsPlaying = false;
        cut.Render();
        cut.Find(".now-playing-icon").ClassList.ShouldContain("icon-pause");

        cut.Instance.Dispose();
    }
}
