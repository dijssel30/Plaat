using Plaat.Contract;
using Plaat.Mobile.Services.Player;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.Tests;

public sealed class PlayerQueueServiceTests
{
    private static TrackDto Track(uint id)
    {
        return new TrackDto
        {
            Id = id,
            Title = $"Track {id}",
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

    private static PlayerQueueService CreateQueueWith(int count)
    {
        var service = new PlayerQueueService();
        service.SetPlayList([.. Enumerable.Range(1, count).Select(i => Track((uint)i))]);
        return service;
    }

    [Fact]
    public void Shuffle_PreservesOriginalOrder_ForUnshuffle()
    {
        var queue = CreateQueueWith(5);
        var originalIds = queue.Select(t => t.Id).ToList();
        queue.Play(2);

        queue.IsShuffled = true;

        queue.IsShuffled.ShouldBeTrue();
        queue.OriginalOrder!.Select(t => t.Id).ShouldBe(originalIds);

        queue.IsShuffled = false;

        queue.Select(t => t.Id).ShouldBe(originalIds);
        queue.OriginalOrder.ShouldBeNull();
    }

    [Fact]
    public void Shuffle_MovesCurrentTrackToFront_AndUpdatesCurrentIndex()
    {
        var queue = CreateQueueWith(6);
        queue.Play(3);
        var currentTrackId = queue.CurrentValue!.Id;

        queue.IsShuffled = true;

        queue.CurrentIndex.ShouldBe(0);
        queue.CurrentValue!.Id.ShouldBe(currentTrackId);
    }

    [Fact]
    public void ShuffleThenPlay_WithNoCurrentTrackYet_RandomizesWhichTrackPlaysFirst()
    {
        // Regression test for the "shuffle always plays the first playlist track" bug:
        // PlaylistDetailViewModel/AlbumDetailViewModel's Shuffle command calls SetPlayList
        // (which clears CurrentIndex to -1) then sets IsShuffled = true, then Play(0) - in that
        // order. If IsShuffled were set to true *after* Play(0) instead, Shuffle() would see a
        // non-null CurrentValue and always pin the original first track back to index 0.
        var originalFirstId = Track(1).Id;
        var firstPlayedIds = new HashSet<uint>();

        for (var i = 0; i < 30; i++)
        {
            var queue = CreateQueueWith(10);
            queue.IsShuffled = true;
            queue.Play(0);
            firstPlayedIds.Add(queue.CurrentValue!.Id);
        }

        firstPlayedIds.ShouldNotBe(new HashSet<uint> { originalFirstId });
    }

    [Fact]
    public void Unshuffle_RestoresCurrentIndex_ToTrackPositionInOriginalOrder()
    {
        var queue = CreateQueueWith(5);
        queue.Play(0);
        var trackId = queue.CurrentValue!.Id;

        queue.IsShuffled = true;
        queue.IsShuffled = false;

        queue.CurrentValue!.Id.ShouldBe(trackId);
        queue.CurrentIndex.ShouldBe(queue.Select(t => t.Id).ToList().IndexOf(trackId));
    }

    [Theory]
    [InlineData(RepeatMode.One, TrackEndedAction.RepeatTrack)]
    [InlineData(RepeatMode.All, TrackEndedAction.Advanced)]
    public void HandleTrackEnded_MidQueue_HonorsRepeatMode(RepeatMode mode, TrackEndedAction expected)
    {
        var queue = CreateQueueWith(3);
        queue.RepeatMode = mode;
        queue.Play(1);

        var result = queue.HandleTrackEnded();

        result.ShouldBe(expected);
    }

    [Fact]
    public void HandleTrackEnded_AtLastTrack_RepeatOff_Stops()
    {
        var queue = CreateQueueWith(3);
        queue.RepeatMode = RepeatMode.Off;
        queue.Play(2);

        var result = queue.HandleTrackEnded();

        result.ShouldBe(TrackEndedAction.Stopped);
        queue.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void HandleTrackEnded_AtLastTrack_RepeatAll_WrapsToFirst()
    {
        var queue = CreateQueueWith(3);
        queue.RepeatMode = RepeatMode.All;
        queue.Play(2);

        var result = queue.HandleTrackEnded();

        result.ShouldBe(TrackEndedAction.Advanced);
        queue.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void HandleTrackEnded_EmptyQueue_Stops()
    {
        var queue = new PlayerQueueService();

        queue.HandleTrackEnded().ShouldBe(TrackEndedAction.Stopped);
    }

    [Fact]
    public void RemoveAt_BeforeCurrent_ShiftsCurrentIndexDown()
    {
        var queue = CreateQueueWith(5);
        queue.Play(3);

        queue.RemoveAt(0);

        queue.CurrentIndex.ShouldBe(2);
        queue.Count.ShouldBe(4);
    }

    [Fact]
    public void RemoveAt_CurrentTrack_ClearsCurrentIndexAndStopsPlayback()
    {
        var queue = CreateQueueWith(4);
        queue.Play(1);

        queue.RemoveAt(1);

        queue.CurrentIndex.ShouldBe(-1);
        queue.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void RemoveAt_AfterCurrent_DoesNotChangeCurrentIndex()
    {
        var queue = CreateQueueWith(5);
        queue.Play(1);

        queue.RemoveAt(3);

        queue.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Move_CurrentTrack_UpdatesCurrentIndexToDestination()
    {
        var queue = CreateQueueWith(5);
        queue.Play(1);

        queue.Move(1, 3);

        queue.CurrentIndex.ShouldBe(3);
    }

    [Fact]
    public void Move_AcrossCurrentIndex_Forward_ShiftsCurrentIndexDown()
    {
        var queue = CreateQueueWith(5);
        queue.Play(2);

        // Moving a track from before the current position to after it shifts current left by one.
        queue.Move(0, 4);

        queue.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Move_AcrossCurrentIndex_Backward_ShiftsCurrentIndexUp()
    {
        var queue = CreateQueueWith(5);
        queue.Play(2);

        // Moving a track from after the current position to before it shifts current right by one.
        queue.Move(4, 0);

        queue.CurrentIndex.ShouldBe(3);
    }

    [Fact]
    public void Next_WrapsAroundToStart()
    {
        var queue = CreateQueueWith(3);
        queue.Play(2);

        queue.Next();

        queue.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void Previous_WrapsAroundToEnd()
    {
        var queue = CreateQueueWith(3);
        queue.Play(0);

        queue.Previous();

        queue.CurrentIndex.ShouldBe(2);
    }

    [Fact]
    public void PlaylistChanged_FiresOnMutation()
    {
        var queue = CreateQueueWith(2);
        var fired = false;
        queue.PlaylistChanged += () => fired = true;

        queue.Play(0);

        fired.ShouldBeTrue();
    }
}
