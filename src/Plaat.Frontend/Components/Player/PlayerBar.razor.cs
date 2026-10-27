using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Player;

public partial class PlayerBar : ComponentBase, IDisposable
{
    [Inject]
    public required IJSRuntime JsRuntime { get; init; }

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    [Inject]
    public required TrackRatingState TrackRatings { get; init; }

    [Inject]
    public required ILogger<PlayerBar> Logger { get; init; }

    [Inject]
    public required ISnackbar Snackbar { get; init; }

    // Every audioPlayer.* call is a SignalR round trip that would otherwise inherit
    // CircuitOptions.JSInteropDefaultCallTimeout (one minute). A tick that waits that long is
    // useless anyway - fail it fast and take the next sample instead.
    private static readonly TimeSpan JsInteropTimeout = TimeSpan.FromSeconds(2);

    // The position push is telemetry sent twice a second; without an explicit budget it inherits
    // HttpClient's 100s default and stalls the whole loop behind one unlucky request.
    private static readonly TimeSpan StatePushTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueuePushTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RatingRequestTimeout = TimeSpan.FromSeconds(5);

    private const int MaxUpdateLoopRestarts = 5;

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private double _currentTime;
    private double _duration;
    private ElementReference _audioRef;
    private double _volume = 1.0;

    private bool _isFullscreen;
    private bool _isQueueOpen;

    // The current track's star rating, and the track it was loaded for, so a re-render that doesn't
    // change tracks (every 500ms tick does one) doesn't re-fetch it.
    private int _rating;
    private ulong _ratingTrackId;
    private bool _savingRating;

    private DotNetObjectReference<PlayerBar>? _dotNetRef;
    private ulong _lastMediaSessionTrackId;
    private bool? _lastMediaSessionIsPlaying;

    // The track id currently loaded into the (single, persistent) <audio> element, so a re-render
    // that doesn't change tracks (every 500ms tick does one) doesn't reload it.
    private ulong _lastLoadedTrackId;

    // Read once at player init (and after a Settings-page change, on next reload) rather than
    // re-checked per track - simple enough for a single-user app; toggling takes effect on the
    // next track change or app restart, not the exact millisecond it's flipped.
    private bool _loudnessNormalizationEnabled = true;
    private ulong _lastVolumeAppliedTrackId;

    // Set while OnInitializedAsync rebuilds the queue from the backend-persisted state, so the
    // SetPlayList/CurrentIndex writes it does don't immediately echo back a half-built queue.
    private bool _restoringState;

    private async Task ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        // On mobile both this and the queue panel cover the full screen, so only one should be
        // up at a time; on desktop the queue is a small popover and can stay open alongside it.
        if (_isFullscreen && _isQueueOpen && await IsMobileViewport())
        {
            _isQueueOpen = false;
        }

        await UpdateBodyScrollLock();
    }

    private async Task OnQueueOpenChanged(bool isOpen)
    {
        _isQueueOpen = isOpen;

        if (_isQueueOpen && _isFullscreen && await IsMobileViewport())
        {
            _isFullscreen = false;
        }

        await UpdateBodyScrollLock();
    }

    private async Task UpdateBodyScrollLock()
    {
        var locked = _isFullscreen || (_isQueueOpen && await IsMobileViewport());
        await InvokeAudioPlayerVoidAsync("uiHelpers.setBodyScrollLocked", locked);
    }

    private async Task<bool> IsMobileViewport()
    {
        try
        {
            return await JsRuntime.InvokeAsync<bool>("uiHelpers.isMobileViewport");
        }
        catch (JSException ex)
        {
            LogIsMobileViewportFailed(Logger, ex);
            return false;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// The current track's admin-set playback trim points, or null when there is no current track /
    /// no trim set for it.
    /// </summary>
    private TimeSpan? CurrentTrimStart
    {
        get
        {
            return MusicPlayList.CurrentIndex >= 0 ? MusicPlayList.CurrentValue.TrimStart : null;
        }
    }

    private TimeSpan? CurrentTrimEnd
    {
        get
        {
            return MusicPlayList.CurrentIndex >= 0 ? MusicPlayList.CurrentValue.TrimEnd : null;
        }
    }

    /// <summary>
    /// The playable window for the current track, in absolute seconds - defaults to the whole file
    /// when no trim is set. The seek bar scopes its progress fraction to this window; the time
    /// labels keep showing these (real/absolute) values rather than window-relative ones.
    /// </summary>
    private double EffectiveStart
    {
        get
        {
            return CurrentTrimStart?.TotalSeconds ?? 0;
        }
    }

    private double EffectiveEnd
    {
        get
        {
            return CurrentTrimEnd?.TotalSeconds ?? _duration;
        }
    }

    /// <summary>
    /// Clamps a position to the playable window. The upper bound is only applied once it is known:
    /// with no trim end and no duration yet (a loaded track that hasn't played, so Tick() hasn't
    /// filled it in), the window is open-ended rather than collapsing to its start.
    /// </summary>
    private double ClampToWindow(double value)
    {
        var start = EffectiveStart;
        var end = EffectiveEnd;

        if (end <= 0)
        {
            return Math.Max(value, start);
        }

        return Math.Clamp(value, start, Math.Max(start, end));
    }

    protected override async Task OnInitializedAsync()
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        _ = RunUpdateLoopSafely();

        MusicPlayList.OnPlaylistChanged += MusicPlayListUpdate;
        TrackRatings.OnRatingChanged += OnRatingChanged;

        try
        {
            var currentUser = await PlaatClient.GetCurrentUser(CancellationToken.None);

            if (currentUser is not null)
            {
                _loudnessNormalizationEnabled = currentUser.LoudnessNormalizationEnabled;
            }
        }
        catch (Exception ex)
        {
            LogLoadLoudnessFailed(Logger, ex);
        }

        _restoringState = true;
        try
        {
            await RestoreQueueFromBackend();
        }
        finally
        {
            _restoringState = false;
        }

        await RefreshRatingIfTrackChanged();

        await InvokeAsync(StateHasChanged);
    }

    private async Task RestoreQueueFromBackend()
    {
        var state = await PlaatClient.GetCurrentState(CancellationToken.None);

        if (state is not null)
        {
            var neededIds = state.TrackIds
                .Concat(state.OriginalTrackIds ?? [])
                .Distinct()
                .ToArray();

            var tracksById = (await PlaatClient.GetTracksByIds(neededIds, CancellationToken.None))
                .ToDictionary(t => t.Id);

            var playList = state.TrackIds
                .Select(tracksById.GetValueOrDefault)
                .OfType<TrackDto>()
                .ToList();

            List<TrackDto>? originalOrder = null;
            if (state is { IsShuffled: true, OriginalTrackIds: not null })
            {
                originalOrder =
                [
                    .. state.OriginalTrackIds
                        .Select(tracksById.GetValueOrDefault)
                        .OfType<TrackDto>()
                ];
            }

            MusicPlayList.SetPlayList(playList, originalOrder);

            MusicPlayList.CurrentIndex = playList.Select(l => l.Id).ToList().IndexOf(state.CurrentTrack);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task MusicPlayListUpdate()
    {
        await PushPlaylist();
        await RefreshRatingIfTrackChanged();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Loads the star rating of whatever track is playing now. Ratings are per user and not carried
    /// on <see cref="TrackDto"/> (the track lists are cached process-wide, shared by every user), so
    /// the player fetches the one track's rating itself whenever the track changes.
    /// </summary>
    private async Task RefreshRatingIfTrackChanged()
    {
        var trackId = MusicPlayList.CurrentIndex >= 0 ? MusicPlayList.CurrentValueId : 0;

        if (trackId == _ratingTrackId)
        {
            return;
        }

        _ratingTrackId = trackId;
        _rating = 0;

        if (trackId == 0)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(RatingRequestTimeout);

            var rating = await TrackRatings.GetRating((uint)trackId, timeout.Token);

            // The track may have moved on while the fetch was in flight - don't show its rating
            // against a different track.
            if (_ratingTrackId == trackId)
            {
                _rating = rating;
            }
        }
        catch (Exception ex)
        {
            LogLoadRatingFailed(Logger, ex, trackId);
        }
    }

    /// <summary>
    /// Keeps the displayed rating in step with an edit made in the track details modal.
    /// </summary>
    private async Task OnRatingChanged(uint trackId, int stars)
    {
        if (trackId != _ratingTrackId)
        {
            return;
        }

        _rating = stars;
        await InvokeAsync(StateHasChanged);
    }

    private string RatingLabel()
    {
        return $"Rated {_rating} out of {TrackRatingDto.MaxStars} stars";
    }

    /// <summary>
    /// Persists a rating set from the bar (or the fullscreen player) for the track playing now.
    /// Goes through <see cref="TrackRatingState"/> like every other write, so a track details modal
    /// open on the same track follows along.
    /// </summary>
    private async Task SetRating(int stars)
    {
        if (MusicPlayList.CurrentIndex < 0 || _savingRating)
        {
            return;
        }

        var trackId = MusicPlayList.CurrentValueId;
        var previous = _rating;

        // Optimistic: the stars follow the gesture straight away and only roll back if the save
        // fails.
        _rating = stars;
        _savingRating = true;

        try
        {
            using var timeout = new CancellationTokenSource(RatingRequestTimeout);

            if (!await TrackRatings.SetRating((uint)trackId, stars, timeout.Token))
            {
                _rating = previous;
                Snackbar.Add("Couldn't save the rating: this track no longer exists.", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            _rating = previous;
            LogSaveRatingFailed(Logger, ex, trackId);
            Snackbar.Add("Couldn't save the rating.", Severity.Error);
        }
        finally
        {
            _savingRating = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);

            if (await WaitForAudioPlayerReadyAsync())
            {
                try
                {
                    await JsRuntime.InvokeVoidAsync("audioPlayer.setMediaSessionActionHandlers", _dotNetRef);
                }
                catch (JSException ex)
                {
                    LogMediaSessionHandlersFailed(Logger, ex);
                }
            }
            else
            {
                LogJsModuleNotReady(Logger);
            }
        }

        // Must run before the firstRender seek-restore and the play() call below, both of which
        // need the element to already have the right track loaded.
        await LoadTrackIfChanged();

        if (firstRender)
        {
            var state = await PlaatClient.GetCurrentState(CancellationToken.None);

            if (state is not null)
            {
                await Seek(state.CurrentSecond);
            }
        }

        if (MusicPlayList is { IsPlaying: true, CurrentIndex: >= 0 })
        {
            await InvokeAudioPlayerVoidAsync("audioPlayer.play", _audioRef, _dotNetRef);
        }

        await UpdateMediaSessionMetadataIfChanged();
        await UpdateMediaSessionPlaybackStateIfChanged();
        await ApplyEffectiveVolumeIfTrackChanged();
    }

    /// <summary>
    /// Polls the tiny inline `audioPlayerReady` check (always available, since it's inlined rather
    /// than loaded from an external file) until `audioPlayer.js` itself has finished loading, so the
    /// very first JS-interop call of the component's lifetime doesn't race the script tag.
    /// </summary>
    private async Task<bool> WaitForAudioPlayerReadyAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (await JsRuntime.InvokeAsync<bool>("audioPlayerReady"))
                {
                    return true;
                }
            }
            catch (JSException)
            {
                // audioPlayerReady itself hasn't registered yet on this very first tick; keep retrying.
            }

            await Task.Delay(50);
        }

        return false;
    }

    [JSInvokable]
    public void OnPlaybackError(string message)
    {
        Snackbar.Add($"Couldn't play this track: {message}", Severity.Error);
    }

    /// <summary>
    /// Every other audioPlayer.* call site below goes through here rather than calling
    /// JsRuntime.InvokeVoidAsync directly, so a not-yet-loaded audioPlayer.js (or a mid-tick
    /// circuit disconnect) logs a warning instead of throwing an uncaught JSException that would
    /// kill the whole circuit.
    /// </summary>
    private async Task InvokeAudioPlayerVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync(identifier, JsInteropTimeout, args);
        }
        catch (JSException ex)
        {
            LogJsInteropFailed(Logger, ex, identifier);
        }
        catch (JSDisconnectedException)
        {
            // expected when the circuit disconnects mid-call
        }
        catch (OperationCanceledException)
        {
            // JsInteropTimeout elapsed - the circuit is stalled, not broken. Every audioPlayer.*
            // function returns immediately (even play(), which doesn't await the media promise),
            // so the only thing being timed here is the round trip.
            LogJsInteropTimedOut(Logger, identifier);
        }
    }

    /// <summary>
    /// Reads a number out of the audio element, or null when there was no reading to be had.
    /// Null rather than 0 on purpose: a failed read used to collapse the position to zero, which
    /// both wiped the displayed progress and pushed a second the backend cannot use towards the
    /// play-count threshold (it counts distinct positions, so a repeated 0 contributes nothing).
    /// </summary>
    private async Task<double?> TryGetAudioPlayerDoubleAsync(string identifier, params object?[] args)
    {
        try
        {
            return await JsRuntime.InvokeAsync<double>(identifier, JsInteropTimeout, args);
        }
        catch (JSException ex)
        {
            LogJsInteropFailed(Logger, ex, identifier);
            return null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            LogJsInteropTimedOut(Logger, identifier);
            return null;
        }
    }

    private async Task UpdateMediaSessionMetadataIfChanged()
    {
        if (MusicPlayList.CurrentIndex < 0 || MusicPlayList.CurrentValue is null)
        {
            return;
        }

        if (_lastMediaSessionTrackId == MusicPlayList.CurrentValue.Id)
        {
            return;
        }

        _lastMediaSessionTrackId = MusicPlayList.CurrentValue.Id;

        await InvokeAudioPlayerVoidAsync(
            "audioPlayer.setMediaSessionMetadata",
            MusicPlayList.CurrentValue.Title,
            MusicPlayList.CurrentValue.AllArtists,
            MusicPlayList.CurrentValue.Album,
            GetImg());
    }

    private async Task UpdateMediaSessionPlaybackStateIfChanged()
    {
        if (MusicPlayList.CurrentIndex < 0)
        {
            return;
        }

        if (_lastMediaSessionIsPlaying == MusicPlayList.IsPlaying)
        {
            return;
        }

        _lastMediaSessionIsPlaying = MusicPlayList.IsPlaying;

        await InvokeAudioPlayerVoidAsync(
            "audioPlayer.setMediaSessionPlaybackState",
            MusicPlayList.IsPlaying ? "playing" : "paused");
    }

    /// <summary>
    /// Loads the current track's audio into the (single, persistent) &lt;audio&gt; element whenever
    /// the current track changes. Only updates <see cref="_lastLoadedTrackId"/> on success, so a
    /// failed/timed-out call is retried on the next render instead of leaving the element stuck
    /// without a source.
    /// </summary>
    private async Task LoadTrackIfChanged()
    {
        if (MusicPlayList.CurrentIndex < 0)
        {
            return;
        }

        var trackId = MusicPlayList.CurrentValueId;

        if (trackId == _lastLoadedTrackId)
        {
            return;
        }

        await EnsureProxySessionFresh();

        try
        {
            await JsRuntime.InvokeVoidAsync("audioPlayer.loadTrack", JsInteropTimeout, _audioRef, GetStreamUrl());
            _lastLoadedTrackId = trackId;

            var start = EffectiveStart;

            if (start > 0)
            {
                await JsRuntime.InvokeVoidAsync("audioPlayer.setCurrentTime", JsInteropTimeout, _audioRef, start);
                _currentTime = start;
            }
        }
        catch (JSException ex)
        {
            LogJsInteropFailed(Logger, ex, "audioPlayer.loadTrack");
        }
        catch (JSDisconnectedException)
        {
            // expected when the circuit disconnects mid-call
        }
        catch (OperationCanceledException)
        {
            LogJsInteropTimedOut(Logger, "audioPlayer.loadTrack");
        }
    }

    /// <summary>
    /// Re-applies the effective (attenuated) volume whenever the current track changes - needed
    /// because the &lt;audio&gt; element is @key'd on track id, so Blazor recreates the DOM node
    /// (and its volume property) on every track change.
    /// </summary>
    private async Task ApplyEffectiveVolumeIfTrackChanged()
    {
        if (MusicPlayList.CurrentIndex < 0 || MusicPlayList.CurrentValue is null)
        {
            return;
        }

        if (_lastVolumeAppliedTrackId == MusicPlayList.CurrentValue.Id)
        {
            return;
        }

        _lastVolumeAppliedTrackId = MusicPlayList.CurrentValue.Id;

        await InvokeAudioPlayerVoidAsync("audioPlayer.setVolume", _audioRef, EffectiveVolume());
    }

    /// <summary>
    /// The user's own volume, scaled down (never up) by the current track's loudness-normalization
    /// attenuation factor, unless the user has disabled normalization on the Settings page.
    /// </summary>
    private double EffectiveVolume()
    {
        return MusicPlayList.CurrentIndex < 0 || !_loudnessNormalizationEnabled
            ? _volume
            : _volume * Math.Min(1.0, MusicPlayList.CurrentValue.AttenuationFactor);
    }

    private void ToggleShuffle()
    {
        MusicPlayList.IsShuffled = !MusicPlayList.IsShuffled;
    }

    private void ToggleRepeat()
    {
        MusicPlayList.RepeatMode = MusicPlayList.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
    }

    private static string GetRepeatTitle(RepeatMode mode)
    {
        return mode switch
        {
            RepeatMode.Off => "Repeat off",
            RepeatMode.All => "Repeat all",
            _ => "Repeat one"
        };
    }

    private async Task TrackEnded()
    {
        switch (MusicPlayList.HandleTrackEnded())
        {
            case TrackEndedAction.RepeatTrack:
                await Seek(EffectiveStart);
                await InvokeAudioPlayerVoidAsync("audioPlayer.play", _audioRef, _dotNetRef);
                break;
            case TrackEndedAction.Stopped:
                // Explicitly pause: this can now run before the audio element reaches the real end
                // of file (a trim-end cutoff from Tick(), rather than the native `ended` event), so
                // playback isn't guaranteed to have already stopped on its own.
                await InvokeAudioPlayerVoidAsync("audioPlayer.pause", _audioRef);
                _currentTime = 0;
                _duration = 0;
                break;
        }
    }

    private void Next()
    {
        if (MusicPlayList.Count == 0)
        {
            return;
        }

        _currentTime = 0;
        _duration = 0;

        MusicPlayList.Next();
        MusicPlayList.IsPlaying = true;
        // Play is triggered by OnAfterRenderAsync once the new audio element is rendered.
    }

    private void Previous()
    {
        if (MusicPlayList.Count == 0)
        {
            return;
        }

        _currentTime = 0;
        _duration = 0;

        MusicPlayList.Previous();
        MusicPlayList.IsPlaying = true;
        // Play is triggered by OnAfterRenderAsync once the new audio element is rendered.
    }

    private async Task TogglePlay()
    {
        if (MusicPlayList.CurrentIndex < 0)
        {
            return;
        }

        MusicPlayList.IsPlaying = !MusicPlayList.IsPlaying;

        if (MusicPlayList.IsPlaying)
        {
            // Re-apply the known position before playing: after a refresh the audio element is
            // freshly created and may not have honored the earlier seek (e.g. because it hadn't
            // loaded any data yet), so playing without this can silently restart at 0.
            await EnsureProxySessionFresh();
            await InvokeAudioPlayerVoidAsync("audioPlayer.setCurrentTime", _audioRef, _currentTime);
            await InvokeAudioPlayerVoidAsync("audioPlayer.play", _audioRef, _dotNetRef);
        }
        else
        {
            await InvokeAudioPlayerVoidAsync("audioPlayer.pause", _audioRef);
        }
    }

    private async Task Close()
    {
        if (MusicPlayList.CurrentIndex >= 0)
        {
            await InvokeAudioPlayerVoidAsync("audioPlayer.pause", _audioRef);
        }

        if (_isFullscreen || _isQueueOpen)
        {
            _isFullscreen = false;
            _isQueueOpen = false;
            await UpdateBodyScrollLock();
        }

        _currentTime = 0;
        _duration = 0;
        _lastMediaSessionTrackId = 0;
        _lastMediaSessionIsPlaying = null;
        _lastLoadedTrackId = 0;
        _rating = 0;
        _ratingTrackId = 0;
        _savingRating = false;

        MusicPlayList.Clear();

        try
        {
            await PlaatClient.ClearCurrentState(CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogClearPlaybackStateFailed(Logger, ex);
        }
    }

    [JSInvokable]
    public async Task MediaSessionPlay()
    {
        if (MusicPlayList.CurrentIndex < 0 || MusicPlayList.IsPlaying)
        {
            return;
        }

        MusicPlayList.IsPlaying = true;
        await EnsureProxySessionFresh();
        await InvokeAudioPlayerVoidAsync("audioPlayer.setCurrentTime", _audioRef, _currentTime);
        await InvokeAudioPlayerVoidAsync("audioPlayer.play", _audioRef, _dotNetRef);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task MediaSessionPause()
    {
        if (!MusicPlayList.IsPlaying)
        {
            return;
        }

        MusicPlayList.IsPlaying = false;
        await InvokeAudioPlayerVoidAsync("audioPlayer.pause", _audioRef);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task MediaSessionNext()
    {
        Next();
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task MediaSessionPrevious()
    {
        Previous();
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task MediaSessionSeek(double time)
    {
        await Seek(time);
    }


    /// <summary>
    /// Supervises <see cref="UpdateLoop"/>. The loop is the only thing that reports playback to
    /// the backend, and the audio element keeps playing perfectly without it, so a loop that ends
    /// early is silent data loss: every remaining track of the session goes uncounted with nothing
    /// to show for it. <see cref="UpdateLoop"/> already survives a failing tick; this is the
    /// backstop for anything that still gets past it.
    /// </summary>
    private async Task RunUpdateLoopSafely()
    {
        for (var attempt = 1; attempt <= MaxUpdateLoopRestarts && !_disposed; attempt++)
        {
            try
            {
                await UpdateLoop();
                return;
            }
            catch (Exception ex)
            {
                if (_disposed)
                {
                    // Disposal races the in-flight tick - expected, and there's nothing left to
                    // restart. Note this is deliberately keyed on _disposed rather than on the
                    // exception type: a JS interop timeout also surfaces as an
                    // OperationCanceledException, and used to be swallowed here as "expected".
                    return;
                }

                LogUpdateLoopTerminated(Logger, ex, attempt, MaxUpdateLoopRestarts);
            }
        }
    }

    private async Task UpdateLoop()
    {
        if (_timer is null || _cts is null)
        {
            return;
        }

        // Only the timer wait sits outside the guard, so cancelling _cts (i.e. Dispose) stays the
        // one way out of this loop.
        while (await _timer.WaitForNextTickAsync(_cts.Token))
        {
            try
            {
                await Tick();
            }
            catch (Exception ex) when (!_disposed)
            {
                LogUpdateTickFailed(Logger, ex);
            }
        }
    }

    /// <summary>
    /// One sample: read the position out of the audio element, report it to the backend (which
    /// derives play counts from these pushes) and refresh the UI. Anything that throws in here
    /// costs this one sample - see <see cref="UpdateLoop"/>.
    /// </summary>
    private async Task Tick()
    {
        if (MusicPlayList.CurrentIndex < 0 || !MusicPlayList.IsPlaying)
        {
            return;
        }

        var time = await TryGetAudioPlayerDoubleAsync("audioPlayer.getCurrentTime", _audioRef);

        if (time is null)
        {
            // No reading this tick (interop timed out, or the circuit is mid-reconnect). Skip the
            // whole sample rather than reporting a position we don't have: the backend counts
            // distinct seconds, so a made-up one is worse than none.
            return;
        }

        var duration = await TryGetAudioPlayerDoubleAsync("audioPlayer.getDuration", _audioRef);

        if (Math.Abs(time.Value - _currentTime) > 0.25)
        {
            _currentTime = time.Value;
        }

        if (duration is not null)
        {
            _duration = duration.Value;
        }

        if (CurrentTrimEnd is { } trimEnd && _currentTime >= trimEnd.TotalSeconds)
        {
            await TrackEnded();
            await InvokeAsync(StateHasChanged);
            return;
        }

        await PushTrackPlayback();

        await InvokeAudioPlayerVoidAsync(
            "audioPlayer.setMediaSessionPositionState", _duration, _currentTime, 1);

        await InvokeAsync(StateHasChanged);
    }

    private async Task Seek(double value)
    {
        var clamped = ClampToWindow(value);
        _currentTime = clamped;

        await InvokeAudioPlayerVoidAsync(
            "audioPlayer.setCurrentTime",
            _audioRef,
            clamped);

        await InvokeAsync(StateHasChanged);
    }

    private async Task SetVolume(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var vol))
        {
            _volume = vol;
            await InvokeAudioPlayerVoidAsync("audioPlayer.setVolume", _audioRef, EffectiveVolume());
        }
    }

    /// <summary>
    /// The browser fetches audio through /proxy/music, which authenticates with a server-side session
    /// that expires with the access token. The token is only refreshed (and the session re-established)
    /// lazily by a PlaatClient call, and a paused player makes none - so after a long pause the first
    /// stream request would hit an expired session and 401. Making one cheap authenticated call first
    /// lets the client refresh the token and re-sync the session before the audio element asks for it.
    /// </summary>
    private async Task EnsureProxySessionFresh()
    {
        try
        {
            await PlaatClient.GetCurrentState(CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogEnsureSessionFailed(Logger, ex);
        }
    }

    private string GetStreamUrl()
    {
        return MusicPlayList.CurrentIndex < 0
            ? null!
            : $"/proxy/music/{MusicPlayList.CurrentValue.Id}";
    }

    private string GetImg()
    {
        return $"/proxy/cover/track/{MusicPlayList.CurrentValue.Id}";
    }

    private string GetAlbumHref()
    {
        return Redirects.GetAlbumHref(MusicPlayList.CurrentValue.AlbumId);
    }

    private string GetVolumeIconClass()
    {
        return _volume == 0 ? "icon-volume-mute" : _volume < 0.5 ? "icon-volume-low" : "icon-volume-high";
    }

    /// <summary>
    /// Pushes just the playback position of the current track. Called on every timer tick, so it
    /// deliberately carries no queue payload - that's <see cref="PushPlaylist"/>'s job.
    /// </summary>
    private async Task PushTrackPlayback()
    {
        if (MusicPlayList.CurrentIndex < 0)
        {
            return;
        }

        var playback = new TrackPlaybackState
        {
            CurrentSecond = _currentTime,
            CurrentTrack = MusicPlayList.CurrentValue.Id
        };

        using var timeout = new CancellationTokenSource(StatePushTimeout);

        try
        {
            await PlaatClient.UpdateCurrentTrackPlayback(playback, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Dropped on purpose: the next tick is 500ms away, so waiting on a slow push only
            // costs us the samples we'd have taken meanwhile.
            LogPositionPushTimedOut(Logger, StatePushTimeout);
        }
        catch (Exception ex)
        {
            LogPositionPushFailed(Logger, ex);
        }
    }

    /// <summary>
    /// Pushes the play queue. Called only from <see cref="MusicPlayListUpdate"/> (the
    /// <see cref="MusicPlaylist.OnPlaylistChanged"/> handler), i.e. only when the queue actually
    /// changes - tracks added/removed/reordered, shuffle toggled, or the current track moved.
    /// </summary>
    private async Task PushPlaylist()
    {
        // The queue is rebuilt from this same backend state during startup; don't echo the
        // half-built queue back while that's in progress, or when it's been emptied (Close()
        // clears the backend state explicitly).
        if (_restoringState || MusicPlayList.Count == 0)
        {
            return;
        }

        var playlist = new PlaylistState
        {
            CurrentTrack = MusicPlayList.CurrentIndex >= 0 ? MusicPlayList.CurrentValue.Id : 0,
            TrackIds = [.. MusicPlayList.Select(l => l.Id)],
            IsShuffled = MusicPlayList.IsShuffled,
            OriginalTrackIds = MusicPlayList.IsShuffled ? [.. MusicPlayList.OriginalOrder!.Select(l => l.Id)] : null
        };

        using var timeout = new CancellationTokenSource(QueuePushTimeout);

        try
        {
            await PlaatClient.UpdateCurrentPlaylist(playlist, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            LogQueuePushTimedOut(Logger, QueuePushTimeout);
        }
        catch (Exception ex)
        {
            LogQueuePushFailed(Logger, ex);
        }
    }

    public void Dispose()
    {
        // Set before cancelling: it's what tells the loop supervisor that an OperationCanceledException
        // is a real shutdown rather than a stalled JS interop call.
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _timer?.Dispose();

        MusicPlayList.OnPlaylistChanged -= MusicPlayListUpdate;
        TrackRatings.OnRatingChanged -= OnRatingChanged;

        _dotNetRef?.Dispose();

        GC.SuppressFinalize(this);
    }

    [LoggerMessage(LogLevel.Warning, "Could not refresh the session before playback")]
    private static partial void LogEnsureSessionFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Failed to load loudness normalization setting; leaving it enabled")]
    private static partial void LogLoadLoudnessFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Failed to load the rating for track {TrackId}")]
    private static partial void LogLoadRatingFailed(ILogger logger, Exception ex, ulong trackId);

    [LoggerMessage(LogLevel.Warning, "Failed to save the rating for track {TrackId}")]
    private static partial void LogSaveRatingFailed(ILogger logger, Exception ex, ulong trackId);

    [LoggerMessage(LogLevel.Warning, "Failed to set media session action handlers")]
    private static partial void LogMediaSessionHandlersFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "audioPlayer JS module was not ready in time; media session handlers not registered")]
    private static partial void LogJsModuleNotReady(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "audioPlayer JS interop call {Identifier} failed")]
    private static partial void LogJsInteropFailed(ILogger logger, Exception ex, string identifier);

    [LoggerMessage(LogLevel.Debug, "audioPlayer JS interop call {Identifier} timed out")]
    private static partial void LogJsInteropTimedOut(ILogger logger, string identifier);

    [LoggerMessage(LogLevel.Warning, "Failed to clear current playback state")]
    private static partial void LogClearPlaybackStateFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Player update loop terminated unexpectedly; restarting ({Attempt} of {Max})")]
    private static partial void LogUpdateLoopTerminated(ILogger logger, Exception ex, int attempt, int max);

    [LoggerMessage(LogLevel.Warning, "Player update tick failed; continuing")]
    private static partial void LogUpdateTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Debug, "Playback position push timed out after {Timeout}")]
    private static partial void LogPositionPushTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(LogLevel.Warning, "Failed to update current playback position")]
    private static partial void LogPositionPushFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Play queue push timed out after {Timeout}")]
    private static partial void LogQueuePushTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(LogLevel.Warning, "Failed to update current playlist state")]
    private static partial void LogQueuePushFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "uiHelpers.isMobileViewport call failed")]
    private static partial void LogIsMobileViewportFailed(ILogger logger, Exception ex);
}
