using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class StateEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // Must match the "sub" claim set up by TestAuthHandler.
    private const int TestUserId = 1;

    private StateContainer StateContainer
    {
        get
        {
            return factory.Services.GetRequiredService<UserStateStore>().GetOrCreate(TestUserId);
        }
    }

    // Reset state before every test by having a constructor-reset approach.
    // Each test is responsible for setting up the state it needs.

    [Fact]
    public async Task GetCurrentState_WhenNoState_ReturnsNoContent()
    {
        StateContainer.State = null;

        var response = await _client.GetAsync("/api/state");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetCurrentState_WhenStateExists_ReturnsOkWithState()
    {
        StateContainer.State = new MusicState
        {
            CurrentTrack = 5,
            CurrentSecond = 42.0,
            TrackIds = [5]
        };

        var response = await _client.GetAsync("/api/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await response.Content.ReadFromJsonAsync<MusicState>();
        state.ShouldNotBeNull();
        state.CurrentTrack.ShouldBe(5u);
        state.CurrentSecond.ShouldBe(42.0);
    }

    [Fact]
    public async Task UpdatePlaylist_SetsQueueButKeepsPositionForSameTrack()
    {
        StateContainer.State = new MusicState { CurrentTrack = 7, CurrentSecond = 33.0, TrackIds = [7] };

        var playlist = new PlaylistState
        {
            CurrentTrack = 7,
            TrackIds = [7, 8, 9]
        };

        var response = await _client.PostAsJsonAsync("/api/state/playlist", playlist);

        response.IsSuccessStatusCode.ShouldBeTrue();
        StateContainer.State.ShouldNotBeNull();
        StateContainer.State.TrackIds.ShouldBe([7u, 8u, 9u]);
        StateContainer.State.CurrentSecond.ShouldBe(33.0);
    }

    [Fact]
    public async Task UpdatePlaylist_WhenCurrentTrackMoved_ResetsPosition()
    {
        StateContainer.State = new MusicState { CurrentTrack = 7, CurrentSecond = 33.0, TrackIds = [7, 8] };

        var playlist = new PlaylistState
        {
            CurrentTrack = 8,
            TrackIds = [7, 8]
        };

        await _client.PostAsJsonAsync("/api/state/playlist", playlist);

        StateContainer.State.ShouldNotBeNull();
        StateContainer.State.CurrentTrack.ShouldBe(8u);
        StateContainer.State.CurrentSecond.ShouldBe(0.0);
    }

    [Fact]
    public async Task UpdateTrackPlayback_SetsPositionAndPreservesQueue()
    {
        StateContainer.State = new MusicState { CurrentTrack = 1, CurrentSecond = 0, TrackIds = [1, 2, 3] };
        StateContainer.CurrentTrack = 1;
        StateContainer.SecondsPlayed = [];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(1, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(4));

        var playback = new TrackPlaybackState { CurrentTrack = 1, CurrentSecond = 10.0 };

        var response = await _client.PostAsJsonAsync("/api/state/track", playback);

        response.IsSuccessStatusCode.ShouldBeTrue();
        StateContainer.State.ShouldNotBeNull();
        StateContainer.State.CurrentSecond.ShouldBe(10.0);
        StateContainer.State.TrackIds.ShouldBe([1u, 2u, 3u]);
    }

    [Fact]
    public async Task UpdateTrackPlayback_WhenTrackChanges_ResetsSecondsPlayed()
    {
        StateContainer.CurrentTrack = 1;
        StateContainer.SecondsPlayed = [5, 10, 15];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(2, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(4));

        var playback = new TrackPlaybackState { CurrentTrack = 2, CurrentSecond = 5.0 };

        await _client.PostAsJsonAsync("/api/state/track", playback);

        StateContainer.CurrentTrack.ShouldBe(2u);
        StateContainer.SecondsPlayed.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateTrackPlayback_WhenSameTrack_AccumulatesSecondsPlayed()
    {
        StateContainer.CurrentTrack = 3;
        StateContainer.SecondsPlayed = [1, 2];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(3, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(4));

        var playback = new TrackPlaybackState { CurrentTrack = 3, CurrentSecond = 5.0 };

        await _client.PostAsJsonAsync("/api/state/track", playback);

        StateContainer.SecondsPlayed.ShouldContain(5);
    }

    [Fact]
    public async Task UpdateTrackPlayback_SecondBeforeTrimStart_IsNotAccumulated()
    {
        StateContainer.CurrentTrack = 4;
        StateContainer.SecondsPlayed = [];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(4, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(2));
        factory.TrackRepository.GetTrimPoints(4, Arg.Any<CancellationToken>())
            .Returns(new TrackTrimPoints(TimeSpan.FromSeconds(10), null));

        var playback = new TrackPlaybackState { CurrentTrack = 4, CurrentSecond = 5.0 };

        await _client.PostAsJsonAsync("/api/state/track", playback);

        StateContainer.SecondsPlayed.ShouldNotContain(5);
    }

    [Fact]
    public async Task UpdateTrackPlayback_SecondAtOrAfterTrimEnd_IsNotAccumulated()
    {
        StateContainer.CurrentTrack = 5;
        StateContainer.SecondsPlayed = [];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(5, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(2));
        factory.TrackRepository.GetTrimPoints(5, Arg.Any<CancellationToken>())
            .Returns(new TrackTrimPoints(null, TimeSpan.FromSeconds(20)));

        var playback = new TrackPlaybackState { CurrentTrack = 5, CurrentSecond = 25.0 };

        await _client.PostAsJsonAsync("/api/state/track", playback);

        StateContainer.SecondsPlayed.ShouldNotContain(25);
    }

    [Fact]
    public async Task UpdateTrackPlayback_TrimmedWindowMostlyWatched_CountsPlayAtTrimmedThreshold()
    {
        // Full duration is 120s (0.4 * 120 = 48s), but the trim window is only 20s long, so the
        // trimmed 40% threshold (8s) - not the full-track one - is what should trigger here.
        StateContainer.CurrentTrack = 6;
        StateContainer.SecondsPlayed = [0, 1, 2, 3, 4, 5, 6, 7];
        StateContainer.PlayCounted = false;

        factory.TrackRepository.GetDuration(6, Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromSeconds(120));
        factory.TrackRepository.GetTrimPoints(6, Arg.Any<CancellationToken>())
            .Returns(new TrackTrimPoints(null, TimeSpan.FromSeconds(20)));

        var playback = new TrackPlaybackState { CurrentTrack = 6, CurrentSecond = 8.0 };

        await _client.PostAsJsonAsync("/api/state/track", playback);

        StateContainer.PlayCounted.ShouldBeTrue();
        await factory.TrackRepository.Received(1).IncreasePlayCount(6, Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
