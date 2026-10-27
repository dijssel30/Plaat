using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Contract;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests;

public sealed class MusicPlaylistTests
{
    private static TrackDto MakeTrack(uint id, string title = "Track")
    {
        return new()
        {
            Id = id,
            Title = title,
            Artists = [],
            Album = "Album",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = id,
            DiscNumber = 1,
            Year = 2024,
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    [Fact]
    public void Count_ReflectsNumberOfTracks()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);

        playlist.Count.ShouldBe(3);
    }

    [Fact]
    public void Next_AdvancesToNextTrack()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 0;

        playlist.Next();

        playlist.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Next_AtLastTrack_WrapsAroundToFirst()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 2;

        playlist.Next();

        playlist.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void Previous_MovesToPreviousTrack()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 2;

        playlist.Previous();

        playlist.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Previous_AtFirstTrack_WrapsAroundToLast()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 0;

        playlist.Previous();

        playlist.CurrentIndex.ShouldBe(2);
    }

    [Fact]
    public void Play_SetsCurrentIndexAndIsPlaying()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);

        playlist.Play(1);

        playlist.CurrentIndex.ShouldBe(1);
        playlist.IsPlaying.ShouldBeTrue();
    }

    [Fact]
    public void Add_IncreasesCountByOne()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1)]);

        playlist.Add(MakeTrack(2));

        playlist.Count.ShouldBe(2);
    }

    [Fact]
    public void AddNext_InsertsTrackAfterCurrentIndex()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(3)]);
        playlist.CurrentIndex = 0;

        playlist.AddNext(MakeTrack(2));

        playlist.Count.ShouldBe(3);
        playlist[1].Id.ShouldBe(2u);
    }

    [Fact]
    public void SetPlayList_ReplacesExistingList()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2)]);

        playlist.SetPlayList([MakeTrack(3), MakeTrack(4), MakeTrack(5)]);

        playlist.Count.ShouldBe(3);
    }

    [Fact]
    public void IsShuffled_WhenNotShuffled_ReturnsFalse()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2)]);

        playlist.IsShuffled.ShouldBeFalse();
    }

    [Fact]
    public void IsShuffled_WhenShuffled_ReturnsTrue()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 0;

        playlist.IsShuffled = true;

        playlist.IsShuffled.ShouldBeTrue();
    }

    [Fact]
    public void Shuffle_KeepsCurrentTrackAtIndexZero()
    {
        var playlist = new MusicPlaylist();
        var tracks = Enumerable.Range(1, 10).Select(i => MakeTrack((uint)i)).ToList();
        playlist.SetPlayList(tracks);
        playlist.CurrentIndex = 3;
        var currentId = playlist.CurrentValue!.Id;

        playlist.IsShuffled = true;

        playlist[0].Id.ShouldBe(currentId);
    }

    [Fact]
    public void Unshuffle_RestoresOriginalOrder()
    {
        var playlist = new MusicPlaylist();
        var tracks = Enumerable.Range(1, 5).Select(i => MakeTrack((uint)i)).ToList();
        playlist.SetPlayList(tracks);
        playlist.CurrentIndex = 0;

        playlist.IsShuffled = true;
        playlist.IsShuffled = false;

        var ids = playlist.Select(t => t.Id).ToList();
        ids.ShouldBe([1u, 2u, 3u, 4u, 5u]);
    }

    [Fact]
    public void Unshuffle_MaintainsCurrentTrack()
    {
        var playlist = new MusicPlaylist();
        var tracks = Enumerable.Range(1, 5).Select(i => MakeTrack((uint)i)).ToList();
        playlist.SetPlayList(tracks);
        playlist.CurrentIndex = 2;
        var originalCurrentId = tracks[2].Id;

        playlist.IsShuffled = true;
        playlist.IsShuffled = false;

        playlist.CurrentValue!.Id.ShouldBe(originalCurrentId);
    }

    [Fact]
    public void CurrentValueId_WhenNoCurrentTrack_ReturnsZero()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1)]);

        playlist.CurrentValueId.ShouldBe(0ul);
    }

    [Fact]
    public void RepeatMode_DefaultsToAll()
    {
        var playlist = new MusicPlaylist();

        playlist.RepeatMode.ShouldBe(RepeatMode.All);
    }

    [Fact]
    public void HandleTrackEnded_RepeatOne_ReturnsRepeatTrackAndKeepsIndex()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 1;
        playlist.RepeatMode = RepeatMode.One;

        var action = playlist.HandleTrackEnded();

        action.ShouldBe(TrackEndedAction.RepeatTrack);
        playlist.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void HandleTrackEnded_RepeatAll_AtLastTrack_WrapsAroundAndAdvances()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 2;
        playlist.RepeatMode = RepeatMode.All;

        var action = playlist.HandleTrackEnded();

        action.ShouldBe(TrackEndedAction.Advanced);
        playlist.CurrentIndex.ShouldBe(0);
        playlist.IsPlaying.ShouldBeTrue();
    }

    [Fact]
    public void HandleTrackEnded_RepeatOff_NotLastTrack_Advances()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 0;
        playlist.RepeatMode = RepeatMode.Off;

        var action = playlist.HandleTrackEnded();

        action.ShouldBe(TrackEndedAction.Advanced);
        playlist.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void HandleTrackEnded_RepeatOff_AtLastTrack_StopsWithoutAdvancing()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 2;
        playlist.RepeatMode = RepeatMode.Off;
        playlist.IsPlaying = true;

        var action = playlist.HandleTrackEnded();

        action.ShouldBe(TrackEndedAction.Stopped);
        playlist.CurrentIndex.ShouldBe(2);
        playlist.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void SetPlayList_RaisesOnPlaylistChangedEvent()
    {
        var playlist = new MusicPlaylist();
        var eventRaised = false;
        playlist.OnPlaylistChanged += () =>
        {
            eventRaised = true;
            return Task.CompletedTask;
        };

        playlist.SetPlayList([MakeTrack(1)]);

        eventRaised.ShouldBeTrue();
    }

    [Fact]
    public void SetPlayList_WhenOneSubscriberThrows_OtherSubscriberStillRuns()
    {
        var playlist = new MusicPlaylist();
        var secondSubscriberRan = false;
        playlist.OnPlaylistChanged += () => throw new InvalidOperationException("boom");
        playlist.OnPlaylistChanged += () =>
        {
            secondSubscriberRan = true;
            return Task.CompletedTask;
        };

        Should.NotThrow(() => playlist.SetPlayList([MakeTrack(1)]));

        secondSubscriberRan.ShouldBeTrue();
    }

    [Fact]
    public void Play_WhenSubscriberThrowsAsynchronously_DoesNotThrow()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2)]);
        playlist.OnPlaylistChanged += async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        };

        Should.NotThrow(() => playlist.Play(1));
    }

    [Fact]
    public void RemoveAt_RemovesTrackAtIndex()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);

        playlist.RemoveAt(1);

        playlist.Count.ShouldBe(2);
        playlist.Select(t => t.Id).ShouldBe([1u, 3u]);
    }

    [Fact]
    public void RemoveAt_BeforeCurrentIndex_ShiftsCurrentIndexBack()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 2;

        playlist.RemoveAt(0);

        playlist.CurrentIndex.ShouldBe(1);
        playlist.CurrentValue.Id.ShouldBe(3u);
    }

    [Fact]
    public void RemoveAt_CurrentlyPlayingTrack_ClearsCurrentIndexAndStopsPlaying()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2)]);
        playlist.Play(1);

        playlist.RemoveAt(1);

        playlist.CurrentIndex.ShouldBe(-1);
        playlist.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void RemoveAt_OutOfRange_DoesNothing()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2)]);

        playlist.RemoveAt(5);

        playlist.Count.ShouldBe(2);
    }

    [Fact]
    public void Move_ReordersTracks()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);

        playlist.Move(0, 2);

        playlist.Select(t => t.Id).ShouldBe([2u, 3u, 1u]);
    }

    [Fact]
    public void Move_CurrentlyPlayingTrack_FollowsToNewIndex()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.Play(0);

        playlist.Move(0, 2);

        playlist.CurrentIndex.ShouldBe(2);
        playlist.CurrentValue.Id.ShouldBe(1u);
    }

    [Fact]
    public void Move_TrackAcrossCurrentIndex_AdjustsCurrentIndex()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);
        playlist.CurrentIndex = 1;

        playlist.Move(0, 2);

        playlist.CurrentIndex.ShouldBe(0);
        playlist.CurrentValue.Id.ShouldBe(2u);
    }

    [Fact]
    public void Move_SameIndex_DoesNothing()
    {
        var playlist = new MusicPlaylist();
        playlist.SetPlayList([MakeTrack(1), MakeTrack(2), MakeTrack(3)]);

        playlist.Move(1, 1);

        playlist.Select(t => t.Id).ShouldBe([1u, 2u, 3u]);
    }

    private static MusicPlaylist Queue(params uint[] ids)
    {
        var playlist = new MusicPlaylist(Substitute.For<ILogger<MusicPlaylist>>());
        playlist.SetPlayList([.. ids.Select(id => MakeTrack(id))]);

        return playlist;
    }

    [Fact]
    public void Shuffle_WithNothingPlaying_KeepsNoCurrentTrack()
    {
        var playlist = Queue(1, 2, 3);

        playlist.IsShuffled = true;
        playlist.IsShuffled = true;

        playlist.CurrentIndex.ShouldBe(-1);
        playlist.OriginalOrder!.Select(t => t.Id).ShouldBe([1u, 2u, 3u]);

        playlist.IsShuffled = false;

        playlist.CurrentIndex.ShouldBe(-1);
        playlist.Select(t => t.Id).ShouldBe([1u, 2u, 3u]);
    }

    [Fact]
    public void SetPlayList_WithOriginalOrder_RestoresAShuffledQueueThatCanBeUnshuffled()
    {
        var playlist = new MusicPlaylist();

        playlist.SetPlayList([MakeTrack(3), MakeTrack(1)], [MakeTrack(1), MakeTrack(3)]);

        playlist.IsShuffled.ShouldBeTrue();
        playlist.IsShuffled = false;
        playlist.Select(t => t.Id).ShouldBe([1u, 3u]);
    }

    [Fact]
    public void Add_WhileShuffled_AlsoAppendsToTheOriginalOrder()
    {
        var playlist = Queue(1, 2);
        playlist.IsShuffled = true;

        playlist.Add(MakeTrack(9));

        playlist.OriginalOrder!.Last().Id.ShouldBe(9u);
        playlist.Last().Id.ShouldBe(9u);
    }

    [Fact]
    public void AddNext_WhileShuffledWithNothingPlaying_GoesFirstInBothOrders()
    {
        var playlist = Queue(1, 2);
        playlist.IsShuffled = true;

        playlist.AddNext(MakeTrack(9));

        playlist[0].Id.ShouldBe(9u);
        playlist.OriginalOrder![0].Id.ShouldBe(9u);
    }

    [Fact]
    public void AddNext_WhileShuffledAndPlaying_FollowsTheCurrentTrackInBothOrders()
    {
        var playlist = Queue(1, 2, 3);
        playlist.Play(1);
        playlist.IsShuffled = true;

        playlist.AddNext(MakeTrack(9));

        playlist[playlist.CurrentIndex + 1].Id.ShouldBe(9u);
        playlist.OriginalOrder!.Select(t => t.Id).ShouldBe([1u, 2u, 9u, 3u]);

        playlist.IsShuffled = false;
        playlist.CurrentValueId.ShouldBe(2ul);
    }

    [Fact]
    public void RemoveAt_AfterTheCurrentTrack_KeepsTheCurrentIndexAndUpdatesTheOriginalOrder()
    {
        var playlist = Queue(1, 2, 3);
        playlist.Play(0);
        playlist.IsShuffled = true;
        var removed = playlist[2];

        playlist.RemoveAt(2);

        playlist.CurrentIndex.ShouldBe(0);
        playlist.OriginalOrder!.ShouldNotContain(removed);
    }

    [Fact]
    public void RemoveAt_NegativeIndex_DoesNothing()
    {
        var playlist = Queue(1, 2);

        playlist.RemoveAt(-1);

        playlist.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public void Move_OutOfRange_DoesNothing(int from, int to)
    {
        var playlist = Queue(1, 2);

        playlist.Move(from, to);

        playlist.Select(t => t.Id).ShouldBe([1u, 2u]);
    }

    [Fact]
    public void Move_TrackFromAfterToBeforeTheCurrentOne_ShiftsTheCurrentIndexForward()
    {
        var playlist = Queue(1, 2, 3);
        playlist.Play(1);

        playlist.Move(2, 0);

        playlist.CurrentIndex.ShouldBe(2);
        playlist.CurrentValueId.ShouldBe(2ul);
    }

    [Fact]
    public void Move_TracksThatDoNotCrossTheCurrentOne_LeaveItAlone()
    {
        var playlist = Queue(1, 2, 3, 4);
        playlist.Play(1);

        playlist.Move(2, 3);

        playlist.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Clear_EmptiesTheQueueAndStops()
    {
        var playlist = Queue(1, 2);
        playlist.Play(0);
        playlist.IsShuffled = true;

        playlist.Clear();

        playlist.Count.ShouldBe(0);
        playlist.IsShuffled.ShouldBeFalse();
        playlist.IsPlaying.ShouldBeFalse();
        playlist.CurrentValueId.ShouldBe(0ul);
    }

    [Fact]
    public void HandleTrackEnded_EmptyQueue_Stops()
    {
        new MusicPlaylist().HandleTrackEnded().ShouldBe(TrackEndedAction.Stopped);
    }

    [Fact]
    public void Enumerating_AsNonGenericEnumerable_YieldsTheTracks()
    {
        System.Collections.IEnumerable playlist = Queue(1, 2);

        playlist.Cast<TrackDto>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task FailingSubscribers_WithoutALogger_AreSwallowed()
    {
        var playlist = new MusicPlaylist();
        playlist.OnPlaylistChanged += () => throw new InvalidOperationException("sync");
        playlist.OnPlaylistChanged += () => Task.FromException(new InvalidOperationException("async"));

        Should.NotThrow(() => playlist.Add(MakeTrack(1)));
        await Task.Delay(50);
    }

    [Fact]
    public async Task FailingSubscribers_WithALogger_AreLogged()
    {
        var logger = Substitute.For<ILogger<MusicPlaylist>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var playlist = new MusicPlaylist(logger);
        playlist.OnPlaylistChanged += () => throw new InvalidOperationException("sync");
        playlist.OnPlaylistChanged += () => Task.FromException(new InvalidOperationException("async"));

        playlist.Add(MakeTrack(1));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "Log") < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "Log").ShouldBe(2);
    }
}
