using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests;

/// <summary>
/// The circuit-wide state objects: sidebar playlists (loading, drag-to-add) and star ratings
/// (persisting and broadcasting changes), including subscribers that fail.
/// </summary>
public sealed class StateModelTests
{
    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();

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
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2000,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    private static PlaylistOnlyDto Playlist(uint id, bool isSmart = false)
    {
        return new PlaylistOnlyDto { Id = id, PlaylistName = $"P{id}", IsSmart = isSmart, TrackCount = 0, IsPinned = false };
    }

    private NavPlaylistsState NavState(ILogger<NavPlaylistsState>? logger = null)
    {
        return new NavPlaylistsState(_client, logger ?? Substitute.For<ILogger<NavPlaylistsState>>());
    }

    [Fact]
    public async Task EnsureLoaded_LoadsOnlyOnceEvenWhenCalledConcurrently()
    {
        var release = new TaskCompletionSource<IReadOnlyCollection<PlaylistOnlyDto>>();
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns(release.Task);
        var state = NavState();

        var first = state.EnsureLoadedAsync();
        var second = state.EnsureLoadedAsync();
        release.SetResult([Playlist(1)]);
        await Task.WhenAll(first, second);
        await state.EnsureLoadedAsync();

        state.Playlists.ShouldHaveSingleItem().Id.ShouldBe(1u);
        await _client.Received(1).GetPlaylists(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ReloadsAndNotifiesEverySubscriberEvenIfOneFails()
    {
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns([Playlist(1)], [Playlist(1), Playlist(2)]);
        var logger = Substitute.For<ILogger<NavPlaylistsState>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var state = NavState(logger);
        await state.EnsureLoadedAsync();
        var notified = 0;
        state.OnPlaylistsChanged += () => throw new InvalidOperationException("sync failure");
        state.OnPlaylistsChanged += () => Task.FromException(new InvalidOperationException("async failure"));
        state.OnPlaylistsChanged += () =>
        {
            notified++;
            return Task.CompletedTask;
        };

        await state.RefreshAsync();

        state.Playlists.Count.ShouldBe(2);
        notified.ShouldBe(1);
        await WaitUntil(() => logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)) >= 2);
    }

    [Fact]
    public async Task Refresh_WithoutSubscribers_JustReloads()
    {
        _client.GetPlaylists(Arg.Any<CancellationToken>()).Returns([Playlist(3)]);
        var state = NavState();

        await state.RefreshAsync();

        state.Playlists.Single().Id.ShouldBe(3u);
    }

    [Fact]
    public void Drag_BeginAndEnd_NotifyOnlyOnActualChanges()
    {
        var state = NavState();
        var changes = 0;
        state.OnDragStateChanged += () =>
        {
            changes++;
            return Task.CompletedTask;
        };

        state.EndDrag();
        state.BeginDrag(Track(5));
        state.DraggedTrack!.Id.ShouldBe(5u);
        state.EndDrag();

        state.DraggedTrack.ShouldBeNull();
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task DropOnPlaylist_AddsTheDraggedTrackToARegularPlaylist()
    {
        var state = NavState();
        state.BeginDrag(Track(5));

        (await state.DropOnPlaylistAsync(Playlist(9))).ShouldBeTrue();

        await _client.Received(1).AddToPlaylist(9, 5, Arg.Any<CancellationToken>());
        state.DraggedTrack.ShouldBeNull();
    }

    [Fact]
    public async Task DropOnPlaylist_NothingDraggedOrSmartTarget_DoesNothing()
    {
        var state = NavState();

        (await state.DropOnPlaylistAsync(Playlist(9))).ShouldBeFalse();
        state.BeginDrag(Track(5));
        (await state.DropOnPlaylistAsync(Playlist(10, isSmart: true))).ShouldBeFalse();

        await _client.DidNotReceiveWithAnyArgs().AddToPlaylist(0, 0, CancellationToken.None);
    }

    [Fact]
    public async Task DropOnPlaylist_WhenTheApiFails_ReturnsFalse()
    {
        _client.AddToPlaylist(9, 5, Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException()));
        var state = NavState();
        state.BeginDrag(Track(5));

        (await state.DropOnPlaylistAsync(Playlist(9))).ShouldBeFalse();
    }

    // ---- ratings ----

    [Fact]
    public async Task Rating_GetDelegatesToTheApi()
    {
        _client.GetTrackRating(4, Arg.Any<CancellationToken>()).Returns(3);
        var ratings = new TrackRatingState(_client, Substitute.For<ILogger<TrackRatingState>>());

        (await ratings.GetRating(4)).ShouldBe(3);
    }

    [Theory]
    [InlineData(9, TrackRatingDto.MaxStars)]
    [InlineData(-2, TrackRatingDto.MinStars)]
    [InlineData(3, 3)]
    public async Task Rating_SetClampsPersistsAndBroadcastsEvenIfASubscriberFails(int requested, int stored)
    {
        _client.SetTrackRating(4, stored, Arg.Any<CancellationToken>()).Returns(true);
        var logger = Substitute.For<ILogger<TrackRatingState>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var ratings = new TrackRatingState(_client, logger);
        var seen = new List<(uint, int)>();
        ratings.OnRatingChanged += (_, _) => throw new InvalidOperationException("sync failure");
        ratings.OnRatingChanged += (_, _) => Task.FromException(new InvalidOperationException("async failure"));
        ratings.OnRatingChanged += (id, stars) =>
        {
            seen.Add((id, stars));
            return Task.CompletedTask;
        };

        (await ratings.SetRating(4, requested)).ShouldBeTrue();

        seen.ShouldBe([(4u, stored)]);
        await WaitUntil(() => logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)) >= 2);
    }

    [Fact]
    public async Task Rating_UnknownTrack_ReturnsFalseWithoutBroadcasting()
    {
        _client.SetTrackRating(4, 3, Arg.Any<CancellationToken>()).Returns(false);
        var ratings = new TrackRatingState(_client, Substitute.For<ILogger<TrackRatingState>>());
        var raised = false;
        ratings.OnRatingChanged += (_, _) =>
        {
            raised = true;
            return Task.CompletedTask;
        };

        (await ratings.SetRating(4, 3)).ShouldBeFalse();

        raised.ShouldBeFalse();
    }

    [Fact]
    public async Task Rating_WithoutSubscribers_StillPersists()
    {
        _client.SetTrackRating(4, 1, Arg.Any<CancellationToken>()).Returns(true);

        (await new TrackRatingState(_client, Substitute.For<ILogger<TrackRatingState>>()).SetRating(4, 1)).ShouldBeTrue();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }
}
