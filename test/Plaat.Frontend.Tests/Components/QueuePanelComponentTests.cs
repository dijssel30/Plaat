using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Frontend.Components.Player;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class QueuePanelComponentTests : BunitContext
{
    private readonly MusicPlaylist _playlist = new();

    public QueuePanelComponentTests()
    {
        Services.AddSingleton(Substitute.For<IPlaatClient>());
        Services.AddSingleton(_playlist);
        Services.AddSingleton<NavPlaylistsState>();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void DraggingAnUpNextRow_OntoAnother_ReordersTheQueue_AndNotifiesSubscribers()
    {
        _playlist.SetPlayList([Track(1), Track(2), Track(3), Track(4)]);
        _playlist.Play(0);
        var changes = 0;
        _playlist.OnPlaylistChanged += () =>
        {
            changes++;
            return Task.CompletedTask;
        };

        var cut = RenderOpen();
        Row(cut, 1).DragStart();
        Row(cut, 3).DragOver();
        Row(cut, 3).Drop();

        _playlist.Select(t => t.Id).ShouldBe([1u, 3u, 4u, 2u]);
        _playlist.CurrentValueId.ShouldBe(1u);
        changes.ShouldBe(1);
        cut.FindAll(".queue-panel-title").Select(e => e.TextContent).ShouldBe(["T1", "T3", "T4", "T2"]);
    }

    [Fact]
    public void TheNowPlayingRow_CannotBeDragged_OrDroppedOnto()
    {
        _playlist.SetPlayList([Track(1), Track(2), Track(3)]);
        _playlist.Play(0);

        var cut = RenderOpen();
        var rows = cut.FindAll(".queue-panel-row");
        rows[0].GetAttribute("draggable").ShouldBe("false");
        rows[1].GetAttribute("draggable").ShouldBe("true");

        Row(cut, 0).DragStart();
        Row(cut, 2).Drop();
        _playlist.Select(t => t.Id).ShouldBe([1u, 2u, 3u]);

        Row(cut, 2).DragStart();
        Row(cut, 0).Drop();
        _playlist.Select(t => t.Id).ShouldBe([1u, 2u, 3u]);
    }

    [Fact]
    public void Drop_IsIgnored_WhenTheQueueChangedMidDrag()
    {
        _playlist.SetPlayList([Track(1), Track(2), Track(3), Track(4)]);
        _playlist.Play(0);

        var cut = RenderOpen();
        Row(cut, 1).DragStart();

        // Track 2 gets removed while it's being dragged; index 1 now holds track 3.
        _playlist.RemoveAt(1);
        Row(cut, 2).Drop();

        _playlist.Select(t => t.Id).ShouldBe([1u, 3u, 4u]);
    }

    private IRenderedComponent<QueuePanel> RenderOpen()
    {
        return Render<QueuePanel>(p => p.Add(x => x.IsOpen, true));
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<QueuePanel> cut, int position)
    {
        // Re-queried every time: each drag event re-renders the rows and invalidates earlier handles.
        return cut.FindAll(".queue-panel-row")[position];
    }

    private static TrackDto Track(uint id)
    {
        return new()
        {
            Id = id,
            Title = $"T{id}",
            Artists = [],
            Album = "Test Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = id,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }
}
