using AndroidX.Media3.Session;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.Logging;
using Plaat.Mobile.Services.Auth;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.Services.Player;

/// <summary>
/// Thin façade around a single app-wide CommunityToolkit.Maui <see cref="MediaElement"/> instance
/// (audio-only, so it never needs to be visible - hosted once in AppShell's XAML root and attached
/// here via <see cref="AttachMediaElement"/>). Translates <see cref="PlayerQueueService"/> state
/// changes into playback actions, and MediaElement events back into queue actions - the native
/// equivalent of Plaat.Frontend/Components/Player/PlayerBar.razor.cs's JS-interop-driven <audio />
/// element handling.
/// </summary>
public sealed class PlaybackEngine
{
    private readonly PlayerQueueService _queue;
    private readonly LoopbackProxyService _proxy;
    private readonly AuthService _authService;
    private readonly ILogger<PlaybackEngine> _logger;
    private MediaElement? _mediaElement;
    private ulong _loadedTrackId;
    private bool _loadInFlight;
    private TimeSpan? _pendingResumePosition;

    public PlaybackEngine(
        PlayerQueueService queue,
        LoopbackProxyService proxy,
        AuthService authService,
        ILogger<PlaybackEngine> logger)
    {
        _queue = queue;
        _proxy = proxy;
        _authService = authService;
        _logger = logger;
        _queue.PlaylistChanged += SyncWithQueue;
        // PlaylistChanged only fires on structural queue mutations (add/remove/next/shuffle/...).
        // Play/pause is toggled by just flipping IsPlaying (see MiniPlayerBarViewModel/
        // NowPlayingViewModel.TogglePlayPause), which only raises PropertyChanged - without this,
        // the queue's IsPlaying flag and the actual MediaElement silently drift apart, so the
        // play/pause button stops reliably starting/stopping audio.
        _queue.PropertyChanged += (_, e) =>
        {
            // A CurrentIndex change also raises CurrentValue/CurrentValueId/HasQueue right after it,
            // and RepeatMode/IsShuffled don't change what's loaded or whether it plays - only these
            // two names need a sync, not the whole burst.
            if (e.PropertyName is nameof(PlayerQueueService.CurrentIndex) or nameof(PlayerQueueService.IsPlaying))
            {
                SyncWithQueue();
            }
        };
    }

    public void AttachMediaElement(MediaElement mediaElement)
    {
        if (_mediaElement is not null && !ReferenceEquals(_mediaElement, mediaElement))
        {
            _mediaElement.MediaEnded -= OnMediaEnded;
            _mediaElement.MediaOpened -= OnLoadSettled;
            _mediaElement.MediaFailed -= OnLoadSettled;
            _mediaElement.StateChanged -= OnStateChanged;

            // The newly-attached element's native player starts from scratch with no memory of where
            // the old one was - capture it here so TryApplyPendingResume can seek the reload back to
            // it once it's ready, instead of the track silently restarting from 0:00 (e.g. after a
            // force-close + reopen, which is exactly when this branch runs).
            _pendingResumePosition = _mediaElement.Position;

            // Android recreates HomePage (and its SharedMediaElement) from scratch after the app's
            // task is removed from Recents and relaunched, even though QueueMediaSessionService kept
            // the process (and this old element's native player) alive and playing in the background.
            // Without stopping it here, the old native player keeps running forever - unreachable,
            // since nothing holds a reference to it once _mediaElement below is reassigned - while
            // the newly-attached element starts a second, independent stream of the same track.
            _mediaElement.Stop();
        }

        _mediaElement = mediaElement;
        _mediaElement.MediaEnded += OnMediaEnded;
        _mediaElement.MediaOpened += OnLoadSettled;
        _mediaElement.MediaFailed += OnLoadSettled;
        _mediaElement.StateChanged += OnStateChanged;

        // CommunityToolkit.Maui.MediaElement always builds its own AndroidX.Media3 MediaSession for
        // the ExoPlayer it plays audio through (MediaManager.CreatePlatformView, unconditionally -
        // the isAndroidForegroundServiceEnabled flag only gates its notification, not the session).
        // That session is system-visible, so the OS media area shows a second "now playing" card
        // for it on top of the one QueueMediaSessionService owns - and every time the handler is
        // rebuilt (HomePage recreated after the task is dropped from Recents, a config change, ...)
        // a fresh session with a new id is created while stale ones linger, stacking up several
        // duplicate cards you can swipe between. We drive the whole system integration ourselves via
        // QueueSessionPlayer, so this redundant session is only ever noise: release it as soon as it
        // exists, and again whenever the handler is rebuilt.
        _mediaElement.HandlerChanged -= OnMediaElementHandlerChanged;
        _mediaElement.HandlerChanged += OnMediaElementHandlerChanged;
        SuppressCommunityToolkitMediaSession();

        SyncWithQueue();
    }

    private void OnMediaElementHandlerChanged(object? sender, EventArgs e)
    {
        SuppressCommunityToolkitMediaSession();
    }

#if ANDROID
    private string? _suppressedCtkSessionId;

    /// <summary>
    /// Releases the MediaSession CommunityToolkit.Maui.MediaElement creates for its private
    /// ExoPlayer, so the OS media controls show only the one <see cref="Platforms.Android.Media.QueueMediaSessionService"/>
    /// owns. Reached by reflection - the handler's <c>MediaManager</c> and its <c>session</c> field
    /// are both non-public - and best-effort: a toolkit internals change just means the duplicate
    /// card comes back, logged here, not a crash. Audio is unaffected; the session plays no part in
    /// decode/output, and the toolkit itself only touches it again on its own disposal (a
    /// double-release AndroidX.Media3 absorbs).
    /// </summary>
    private void SuppressCommunityToolkitMediaSession()
    {
        try
        {
            if (_mediaElement?.Handler is not { } handler)
            {
                return;
            }

            const System.Reflection.BindingFlags instanceMembers =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

            var manager = handler.GetType().GetProperty("MediaManager", instanceMembers)?.GetValue(handler);

            if (manager?.GetType().GetField("session", instanceMembers)?.GetValue(manager) is not MediaSession session || session.Id == _suppressedCtkSessionId)
            {
                return;
            }

            _suppressedCtkSessionId = session.Id;
            session.Release();
            _logger.LogInformation("Released CommunityToolkit MediaElement's redundant media session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release CommunityToolkit MediaElement's media session - the OS may show a duplicate now-playing card");
        }
    }
#else
    private void SuppressCommunityToolkitMediaSession()
    {
    }
#endif

    private void SyncWithQueue()
    {
        if (_mediaElement is null)
        {
            return;
        }

        var current = _queue.CurrentValue;

        if (current is null)
        {
            // _loadInFlight is deliberately left alone: Stop() doesn't cancel a Source assignment
            // that's already in flight (it doesn't clear MediaElement.Source, so CommunityToolkit's
            // PlatformUpdateSource still runs to completion and still raises MediaOpened). Clearing
            // the flag here would let the next track's load start while the old one is still
            // pending - exactly the stale-load race the flag exists to prevent. OnLoadSettled clears
            // it and re-runs this method, which then stops again if the queue is still empty.
            _mediaElement.Stop();
            _loadedTrackId = 0;
            return;
        }

        var isNewTrack = current.Id != _loadedTrackId;
        if (isNewTrack)
        {
            if (_loadInFlight)
            {
                // A previous track's platform-side load (MediaElement.Source assignment) hasn't settled
                // yet - CommunityToolkit.Maui.MediaElement's Android PlatformUpdateSource is fire-and-forget
                // (it awaits an async cover-art fetch before touching the native player/notification), so a
                // second Source assignment fired before the first one's continuation runs can let the older
                // load "win" the race and land after the newer one, silently reasserting the stale track over
                // the native player and the system notification even though the queue has already moved on.
                // Leaving _loadedTrackId unchanged means we'll revisit this once the in-flight load settles.
                return;
            }

            _loadedTrackId = current.Id;
            _loadInFlight = true;

            // Default the position a fresh load settles at to the track's trim start (a no-op when
            // there is none). An explicit ResumeAt(...) call from PlaybackStateSyncService (a real
            // cross-device/cold-start resume) runs after this with plain assignment, so it still
            // wins over this default.
            _pendingResumePosition ??= current.TrimStart;

            _mediaElement.MetadataTitle = current.Title;
            _mediaElement.MetadataArtist = current.AllArtists;
            // MetadataArtworkUrl is deliberately left unset. CommunityToolkit downloads it (a HEAD plus
            // a full GET, through the loopback proxy and out to the API) *before* it hands the track to
            // ExoPlayer, so setting it held back the start of every track by a full-size cover download.
            // The artwork would only ever feed CommunityToolkit's own media session, which
            // SuppressCommunityToolkitMediaSession releases anyway - the notification and lock screen get
            // theirs from QueueMediaSessionService/QueueSessionPlayer instead.
            _mediaElement.Source = MediaSource.FromUri(_proxy.MusicUrl(current.Id));

            // Attenuate-only loudness normalization: there's no user volume control on mobile
            // today, so effective volume is just the attenuation factor (1.0/unchanged when
            // unmeasured or when the user disabled normalization on the Settings page).
            _mediaElement.Volume = _authService.LoudnessNormalizationEnabled
                ? Math.Clamp(current.AttenuationFactor, 0.0, 1.0)
                : 1.0;
        }

        _logger.LogInformation(
            "SyncWithQueue: trackId={TrackId} isNewTrack={IsNewTrack} queueIsPlaying={QueueIsPlaying} nativeState={NativeState}",
            current.Id, isNewTrack, _queue.IsPlaying, _mediaElement.CurrentState);

        if (_loadInFlight)
        {
            // Never drive the transport controls while a Source assignment is still settling.
            // MediaElement.Play() maps straight to ExoPlayer's Prepare() + Play(), and it aims at
            // whatever media item the native player is holding *right now*: CommunityToolkit only
            // hands it the newly assigned one at the end of PlatformUpdateSource, which is async (it
            // would also await a MetadataArtworkUrl download here if one were set - see above), so
            // until that settles the player still holds the previous item - or, on the first ever
            // play, none at all.
            //
            // ExoPlayer's Prepare() is a no-op unless the player is IDLE, but IDLE is exactly the
            // state it is in here: before the first item is ever loaded, and right after Stop(),
            // which SetPlayList triggers on every "play this album/list" via CurrentIndex = -1. With
            // an empty timeline Prepare() lands straight on STATE_ENDED, and CommunityToolkit raises
            // MediaEnded off any STATE_ENDED (it has no notion of which item ended) - so OnMediaEnded
            // advances the queue, and the track whose load is still in flight is dropped on the floor:
            // press play on an album and it starts on track 2. With a stale timeline the same call
            // restarts the *previous* track over the top of the new one. Either way the queue and the
            // audio drift apart, which is what made playing an album sound like shuffle had switched
            // itself on.
            //
            // OnLoadSettled re-runs this method once the load lands and starts playback then, so a
            // play/pause tapped meanwhile isn't lost - just applied when there's something to apply
            // it to.
            return;
        }

        if (_queue.IsPlaying)
        {
#if ANDROID
            Platforms.Android.Media.QueueMediaSessionService.EnsureStarted(
                Platform.AppContext);
#endif

            if (_mediaElement.CurrentState is MediaElementState.None or MediaElementState.Stopped or MediaElementState.Failed)
            {
                // The OS can tear down the native player session out from under us (e.g. after the
                // app is backgrounded/screen-locked for a while) even though we still think this
                // track is "loaded" - without this, Play() is called on a session with nothing
                // loaded and silently does nothing, which is why play/pause can appear completely
                // dead after backgrounding the app.
                _logger.LogWarning("Native player state was {State} for an already-loaded track - reloading source before Play()", _mediaElement.CurrentState);
                _loadInFlight = true;
                _mediaElement.Source = MediaSource.FromUri(_proxy.MusicUrl(current.Id));
                return;
            }

            _mediaElement.Play();
        }
        else
        {
            // Only pause when toggling an already-loaded/played track. Calling Pause() on a source
            // that was just assigned and never played (e.g. right after PlaybackStateSyncService
            // restores a paused queue on app startup) can leave some platform players stuck in a
            // state a later Play() doesn't resume from. The _loadInFlight guard above is what makes
            // that true here: reaching this point means the load has settled.
            _mediaElement.Pause();
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (_loadInFlight)
        {
            // Not the end of the track the queue is on: while a load is in flight the native player
            // is still holding the previous item (or none), and CommunityToolkit raises MediaEnded
            // off any ExoPlayer STATE_ENDED with no notion of which item it belonged to. Advancing
            // on one of these would skip the track being loaded right now. See the _loadInFlight
            // comment in SyncWithQueue for how such an end gets raised in the first place.
            _logger.LogWarning("Ignoring MediaEnded raised while track {TrackId}'s load was still in flight", _loadedTrackId);
            return;
        }

        AdvanceOrRepeatOrStop();
    }

    /// <summary>
    /// Checked every tick by <see cref="PlaybackStateSyncService"/>'s always-on position loop (not
    /// tied to any screen being visible), since <see cref="MediaElement"/> only raises
    /// <see cref="MediaElement.MediaEnded"/> at the real end of file - a trimmed track's <c>TrimEnd</c>
    /// is reached long before that, if ever. Returns true when it advanced/repeated/stopped the
    /// queue, so the caller can skip reporting this tick's (now stale) position.
    /// </summary>
    public bool HandleTrimEndIfReached()
    {
        if (_mediaElement is null || _loadInFlight)
        {
            return false;
        }

        if (_queue.CurrentValue?.TrimEnd is not { } trimEnd || Position < trimEnd)
        {
            return false;
        }

        AdvanceOrRepeatOrStop();

        return true;
    }

    private void AdvanceOrRepeatOrStop()
    {
        if (_queue.HandleTrackEnded() == TrackEndedAction.RepeatTrack)
        {
            _ = _mediaElement?.SeekTo(_queue.CurrentValue?.TrimStart ?? TimeSpan.Zero, CancellationToken.None);
            _mediaElement?.Play();
        }
    }

    // Fires once the in-flight Source assignment has actually settled on the native side (loaded or
    // failed). Re-running SyncWithQueue here picks up whatever the queue's CurrentValue is *now* -
    // which may have moved on again while we were waiting - so a train of rapid track changes ends up
    // applied one at a time, in order, instead of racing each other. See the _loadInFlight comment above.
    private void OnLoadSettled(object? sender, EventArgs e)
    {
        _loadInFlight = false;

        TryApplyPendingResume();

        SyncWithQueue();
    }

    private void OnStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        AdoptExternalPlayPause(e);
        TryApplyPendingResume();
    }

    /// <summary>
    /// Adopts a play/pause that happened *to* the native player rather than because we asked for it -
    /// a headset/Bluetooth transport button, or the OS pausing us on audio-focus loss (an incoming
    /// call, another app taking over playback). CommunityToolkit.Maui.MediaElement wraps its private
    /// ExoPlayer in a Media3 session of its own, and Android delivers media-button presses to whichever
    /// session it considers active, so such a press can land straight on that player - pausing the audio
    /// without ever reaching <see cref="Platforms.Android.Media.QueueSessionPlayer"/>.HandleSetPlayWhenReady,
    /// which is the path our own notification's buttons take. Without this the queue keeps believing it
    /// is playing: the mini player and now-playing screen stay showing a pause icon over silence, and the
    /// next in-app tap "pauses" an already-paused track instead of resuming it.
    /// </summary>
    private void AdoptExternalPlayPause(MediaStateChangedEventArgs e)
    {
        // A load that hasn't settled is still coming to rest on whatever ShouldAutoPlay left
        // playWhenReady at, which is not an intent anybody expressed - see SyncWithQueue's
        // _loadInFlight comment. With no track there is nothing to adopt a play/pause for either,
        // and playWhenReady survives the Stop() that emptying the queue issues, so reading it then
        // would resurrect playback over an empty queue.
        if (_loadInFlight || _queue.CurrentValue is null)
        {
            return;
        }

        if (TryGetNativePlayWhenReady() is not { } isPlaying || isPlaying == _queue.IsPlaying)
        {
            // Equal states are the common case: this same event is also how our own Play()/Pause() calls
            // report back, and there the queue already holds the state being reported.
            return;
        }

        _logger.LogInformation(
            "Adopting external play/pause: native player went {Previous}->{New} on its own with playWhenReady={PlayWhenReady}, queue IsPlaying={QueueIsPlaying}",
            e.PreviousState, e.NewState, isPlaying, _queue.IsPlaying);

        // Raises PropertyChanged, so SyncWithQueue runs and issues the matching Play()/Pause() on the
        // native player - which is already in that state, making it a no-op that raises no further
        // state change, so there is no loop back into here.
        _queue.IsPlaying = isPlaying;
    }

    /// <summary>
    /// Reads ExoPlayer's own playWhenReady flag - whether the player has been *told* to play,
    /// independent of whether it currently has data to play - or null when the native player can't be
    /// reached (before the handler exists, on a platform without this hook), in which case no
    /// play/pause is adopted.
    /// <para>
    /// This deliberately doesn't infer the answer from <see cref="MediaElementState"/>:
    /// CommunityToolkit's Android MediaManager.OnPlayerStateChanged maps ExoPlayer's BUFFERING
    /// straight onto <see cref="MediaElementState.Paused"/>, so from the state alone every rebuffer
    /// is indistinguishable from a transport pause. Every seek rebuffers, so
    /// <see cref="AdoptExternalPlayPause"/> used to read a tap on the seek bar as "something paused
    /// us" and stop playback; a network stall mid-track did the same. playWhenReady stays true
    /// through both and only flips when something actually asks the player to stop.
    /// </para>
    /// </summary>
    private bool? TryGetNativePlayWhenReady()
    {
#if ANDROID
        // MediaManager keeps its IExoPlayer protected, but it also hands the very same instance to the
        // PlayerView it builds (MediaManager.CreatePlatformView), and that view is somewhere in the
        // MauiMediaElement view group the handler exposes - so walk to it rather than reflecting into
        // the toolkit's internals.
        if (_mediaElement?.Handler?.PlatformView is global::Android.Views.View platformView)
        {
            return FindPlayerView(platformView)?.Player?.PlayWhenReady;
        }
#endif

        return null;
    }

#if ANDROID
    private static AndroidX.Media3.UI.PlayerView? FindPlayerView(global::Android.Views.View view)
    {
        if (view is AndroidX.Media3.UI.PlayerView playerView)
        {
            return playerView;
        }

        if (view is not global::Android.Views.ViewGroup group)
        {
            return null;
        }

        for (var i = 0; i < group.ChildCount; i++)
        {
            if (group.GetChildAt(i) is { } child && FindPlayerView(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
#endif

    /// <summary>
    /// Seeks to the position a restore (or a re-attach) asked for, but only once the native player
    /// is actually prepared and settled: a seek issued while it's still Opening/Buffering is
    /// silently dropped (there's no timeline to seek in yet), and one issued against a load that's
    /// about to be superseded - the cache restore's source assignment, immediately followed by the
    /// backend reconcile's - is thrown away with that load. Both cost the resume position, so this
    /// waits for a Paused/Playing state with no load still in flight.
    /// </summary>
    private void TryApplyPendingResume()
    {
        if (_loadInFlight
            || _pendingResumePosition is not { } resumePosition
            || _mediaElement?.CurrentState is not (MediaElementState.Paused or MediaElementState.Playing))
        {
            return;
        }

        _pendingResumePosition = null;
        _ = _mediaElement.SeekTo(ClampToTrimWindow(resumePosition), CancellationToken.None);
    }

    /// <summary>
    /// Pulls a position back inside the current track's trim window, if it has one - covers both the
    /// new-track default (see <see cref="SyncWithQueue"/>) and a restored position saved before a
    /// trim window existed, or from outside one added/changed since.
    /// </summary>
    private TimeSpan ClampToTrimWindow(TimeSpan position)
    {
        var start = _queue.CurrentValue?.TrimStart ?? TimeSpan.Zero;
        var end = _queue.CurrentValue?.TrimEnd;

        if (position < start)
        {
            return start;
        }

        if (end is { } e && position > e)
        {
            return e;
        }

        return position;
    }

    public Task SeekTo(TimeSpan position, CancellationToken cancellationToken)
    {
        return _mediaElement?.SeekTo(position, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Records where playback should resume as soon as the current track's source has finished
    /// loading, instead of seeking right now. Restoring a queue (see PlaybackStateSyncService) only
    /// assigns MediaElement.Source; the native player isn't prepared yet, so an immediate
    /// <see cref="SeekTo"/> is either dropped or - on Android - hands back a Task that never
    /// completes, because the seek-completed callback it waits on can only fire once something is
    /// actually loaded. <see cref="TryApplyPendingResume"/> applies this once the player is ready.
    /// </summary>
    public void ResumeAt(TimeSpan position)
    {
        _pendingResumePosition = position;
    }

    public TimeSpan Position
    {
        get
        {
            return _mediaElement?.Position ?? TimeSpan.Zero;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            return _mediaElement?.Duration ?? TimeSpan.Zero;
        }
    }
}
