using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class MusicFileListComponentTests : BunitContext
{
    private readonly IPlaatClient _client;
    private readonly MusicPlaylist _playlist = new();

    public MusicFileListComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        _client.GetPlaylists(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<PlaylistOnlyDto>>([]));

        Services.AddSingleton(_client);
        Services.AddSingleton(_playlist);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
        Services.AddSingleton<NavPlaylistsState>();
        Services.AddSingleton<TrackRatingState>();
        Services.AddSingleton(Substitute.For<ISnackbar>());
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The per-row action menu is the only MudBlazor component in the list; stubbing it keeps
        // the full MudBlazor service graph (and its async-only disposables) out of these tests.
        ComponentFactories.AddStub<MudMenu>();
    }

    [Fact]
    public void ClickingARow_WithoutQueueMusic_QueuesOnlyTheDisplayedTracks()
    {
        var cut = Render<MusicFileList>(p => p
            .Add(x => x.Music, new[] { Track(1), Track(2) })
            .Add(x => x.ShowSearchBar, false));

        cut.FindAll(".track-table-row")[1].Click();

        _playlist.Select(t => t.Id).ShouldBe([1u, 2u]);
        _playlist.CurrentValueId.ShouldBe(2u);
    }

    [Fact]
    public void ClickingARow_WithQueueMusic_QueuesTheTracksThatArentDisplayed_Too()
    {
        var all = new[] { Track(1), Track(2), Track(3), Track(4) };

        var cut = Render<MusicFileList>(p => p
            .Add(x => x.Music, all.Take(2).ToArray())
            .Add(x => x.QueueMusic, all)
            .Add(x => x.ShowSearchBar, false));

        cut.FindAll(".track-table-row").Count.ShouldBe(2);

        cut.FindAll(".track-table-row")[1].Click();

        _playlist.Select(t => t.Id).ShouldBe([1u, 2u, 3u, 4u]);
        _playlist.CurrentValueId.ShouldBe(2u);
    }

    [Fact]
    public void ClickingARow_WithQueueMusic_QueuesInTheActiveSortOrder()
    {
        var all = new[] { Track(1), Track(2), Track(3), Track(4) };

        var cut = Render<MusicFileList>(p => p
            .Add(x => x.Music, all.Take(2).ToArray())
            .Add(x => x.QueueMusic, all)
            .Add(x => x.ShowSearchBar, false));

        // Sort by title descending - "Track 2", "Track 1" on screen, and the queue must follow.
        cut.Find(".sortable.title").Click();
        cut.Find(".sortable.title").Click();

        cut.FindAll(".track-table-row")[0].Click();

        _playlist.Select(t => t.Id).ShouldBe([4u, 3u, 2u, 1u]);
        _playlist.CurrentValueId.ShouldBe(2u);
    }

    private static TrackDto Track(uint id)
    {
        return new()
        {
            Id = id,
            Title = $"Track {id}",
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
