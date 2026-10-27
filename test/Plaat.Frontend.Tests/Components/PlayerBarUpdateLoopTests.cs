using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend.Components.Player;
using Plaat.Frontend.Models;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// Covers the resilience of the player's position loop. That loop is the only thing that reports
/// playback to the backend - and the backend derives play counts from those reports - while the
/// audio element keeps playing perfectly without it. A loop that ends early is therefore silent
/// data loss: every remaining track of the session goes uncounted, with nothing to show for it.
/// </summary>
public sealed class PlayerBarUpdateLoopTests : BunitContext
{
    // The loop ticks every 500ms; give it room for several ticks on a loaded machine.
    private static readonly TimeSpan LoopWindow = TimeSpan.FromSeconds(4);

    private readonly IPlaatClient _client = Substitute.For<IPlaatClient>();

    private int _positionPushes;

    public PlayerBarUpdateLoopTests()
    {
        _client.GetCurrentState(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MusicState?>(null));
        _client.GetTracksByIds(Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TrackDto>>([]));
        _client.UpdateCurrentTrackPlayback(Arg.Any<TrackPlaybackState>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref _positionPushes);
                return Task.CompletedTask;
            });

        Services.AddSingleton(_client);
        Services.AddSingleton(Substitute.For<ISnackbar>());
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
        Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        Services.AddSingleton(PlayingQueue());
        Services.AddSingleton<TrackRatingState>();

        ComponentFactories.AddStub<SeekBar>();
        ComponentFactories.AddStub<QueuePanel>();

        // Loose, so the incidental audioPlayer.* calls (play, media session metadata) are no-ops;
        // the ones this fixture cares about are set up explicitly per test.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("audioPlayerReady").SetResult(true);
    }

    [Fact]
    public async Task UpdateLoop_WhenATickThrows_KeepsReportingPosition()
    {
        JSInterop.Setup<double>("audioPlayer.getCurrentTime", _ => true).SetResult(42);
        JSInterop.Setup<double>("audioPlayer.getDuration", _ => true).SetResult(180);

        // Throws on every tick, after the position has already been read and pushed. Before the
        // tick was guarded, the very first one ended the loop for the life of the circuit.
        JSInterop.SetupVoid("audioPlayer.setMediaSessionPositionState", _ => true)
            .SetException(new InvalidOperationException("interop blew up mid-tick"));

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        // More than one push proves the loop outlived the failing tick rather than reporting once
        // and quietly dying.
        _positionPushes.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task UpdateLoop_WhenPositionReadFails_ReportsNothingRatherThanZero()
    {
        // A JSException is the failure the helper has always handled - by returning 0, which the
        // loop then reported as a real position.
        JSInterop.Setup<double>("audioPlayer.getCurrentTime", _ => true)
            .SetException(new JSException("audioPlayer.js call failed"));
        JSInterop.Setup<double>("audioPlayer.getDuration", _ => true).SetResult(180);

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        // Reporting a fabricated 0 both wiped the displayed position and pushed a second the
        // backend cannot use: it counts distinct positions, so a repeated 0 never reaches the play
        // threshold while still resetting the shared per-user state on every tick.
        _positionPushes.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateLoop_WhenInteropTimesOut_KeepsTicking()
    {
        // A JS interop call that exceeds CircuitOptions.JSInteropDefaultCallTimeout surfaces as a
        // TaskCanceledException. That used to escape the helper and land in a catch that treated
        // it as an expected shutdown, ending the loop silently - no log, no visible symptom, and
        // no play counted for the rest of the circuit.
        JSInterop.Setup<double>("audioPlayer.getCurrentTime", _ => true)
            .SetException(new TaskCanceledException());

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        // Nothing can be pushed while the position is unreadable, so the loop's survival is
        // measured by whether it is still trying.
        JSInterop.Invocations["audioPlayer.getCurrentTime"].Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task UpdateLoop_PositionReachesTrimEnd_StopsAndPausesInsteadOfPlayingToRealEnd()
    {
        // The <audio> element only raises `ended` at the real end of file, which a trimmed track
        // may never reach - Tick() has to notice TrimEnd itself and stop playback there. Uses its
        // own queue content (rather than the shared PlayingQueue() track) so the other tests in
        // this fixture - which rely on getCurrentTime staying below any trim point - are unaffected.
        var queue = Services.GetRequiredService<MusicPlaylist>();
        queue.RepeatMode = RepeatMode.Off;
        queue.SetPlayList([TrimmedTrack()]);
        queue.Play(0);

        JSInterop.Setup<double>("audioPlayer.getCurrentTime", _ => true).SetResult(35);
        JSInterop.Setup<double>("audioPlayer.getDuration", _ => true).SetResult(180);
        JSInterop.SetupVoid("audioPlayer.pause", _ => true);

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        queue.IsPlaying.ShouldBeFalse();
        JSInterop.Invocations["audioPlayer.pause"].ShouldNotBeEmpty();

        // The trim-end check short-circuits before pushing, so the (already past-the-window)
        // position is never reported to the backend.
        _positionPushes.ShouldBe(0);
    }

    [Fact]
    public async Task ApplyEffectiveVolume_NormalizationEnabledAndTrackLouderThanTarget_ScalesVolumeDown()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CurrentUserDto?>(new CurrentUserDto("test", false, true)));

        Services.GetRequiredService<MusicPlaylist>().SetPlayList([LoudTrack()]);
        Services.GetRequiredService<MusicPlaylist>().Play(0);

        JSInterop.SetupVoid("audioPlayer.setVolume", _ => true);

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        var lastCall = JSInterop.Invocations["audioPlayer.setVolume"].Last();
        Convert.ToDouble(lastCall.Arguments[1]).ShouldBeLessThan(1.0);
    }

    [Fact]
    public async Task ApplyEffectiveVolume_NormalizationDisabled_LeavesVolumeUnchanged()
    {
        _client.GetCurrentUser(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CurrentUserDto?>(new CurrentUserDto("test", false, false)));

        Services.GetRequiredService<MusicPlaylist>().SetPlayList([LoudTrack()]);
        Services.GetRequiredService<MusicPlaylist>().Play(0);

        JSInterop.SetupVoid("audioPlayer.setVolume", _ => true);

        Render<PlayerBar>();

        await Task.Delay(LoopWindow);

        var lastCall = JSInterop.Invocations["audioPlayer.setVolume"].Last();
        Convert.ToDouble(lastCall.Arguments[1]).ShouldBe(1.0);
    }

    /// <summary>
    /// A track with an admin-set trim end at 30s, well before its getCurrentTime/getDuration.
    /// </summary>
    private static TrackDto TrimmedTrack()
    {
        return new()
        {
            Id = 2,
            Title = "Trimmed Track",
            Artists = [],
            Album = "Sahara",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2026,
            Duration = TimeSpan.FromMinutes(3),
            TrimEnd = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// A track measured well above the default normalization target, so attenuation kicks in.
    /// </summary>
    private static TrackDto LoudTrack()
    {
        return new()
        {
            Id = 1,
            Title = "Aisha Aisha",
            Artists = [],
            Album = "Sahara",
            AlbumId = 1,
            PlayCount = 0,
            TrackNumber = 1,
            DiscNumber = 1,
            Year = 2026,
            Duration = TimeSpan.FromMinutes(3),
            IntegratedLoudnessLufs = -4f
        };
    }

    /// <summary>
    /// A one-track queue that is already playing, so the loop has something to report.
    /// </summary>
    private static MusicPlaylist PlayingQueue()
    {
        var playlist = new MusicPlaylist();

        playlist.SetPlayList([
            new TrackDto
            {
                Id = 1,
                Title = "Aisha Aisha",
                Artists = [],
                Album = "Sahara",
                AlbumId = 1,
                PlayCount = 0,
                TrackNumber = 1,
                DiscNumber = 1,
                Year = 2026,
                Duration = TimeSpan.FromMinutes(3)
            }
        ]);

        playlist.Play(0);

        return playlist;
    }
}
