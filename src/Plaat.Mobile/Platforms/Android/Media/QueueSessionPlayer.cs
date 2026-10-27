using System.ComponentModel;
using Android.OS;
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using Google.Common.Util.Concurrent;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.Platforms.Android.Media;

/// <summary>
/// A "shadow" Media3 player whose only job is to drive the system media notification / lock-screen
/// widget from our real queue state (title/artist/artwork/position/play state), including a working
/// Next/Previous - CommunityToolkit.Maui.MediaElement's own Android player only ever loads a single
/// MediaItem (no playlist concept at all), so its auto-generated session notification can never show a
/// Next action regardless of queue state (see the comment on PlaybackEngine's _loadInFlight field for
/// the sibling desync issue that came from the same single-item design). Real audio decode/output
/// still happens entirely inside CommunityToolkit's own private ExoPlayer; this class never touches
/// audio - it only reports state and forwards the OS's Play/Pause/Seek/Next/Previous button presses
/// into <see cref="PlayerQueueService"/>/<see cref="PlaybackEngine"/>, which drive the real player the
/// same way the in-app UI does.
/// </summary>
sealed class QueueSessionPlayer : SimpleBasePlayer
{
    private readonly PlayerQueueService _queue;
    private readonly PlaybackEngine _engine;
    private readonly LoopbackProxyService _proxy;
    private readonly Handler _handler;

    // 1 while an InvalidateState is already posted to the looper and hasn't run yet - see PostInvalidate.
    private int _invalidatePending;

    public QueueSessionPlayer(Looper looper, PlayerQueueService queue, PlaybackEngine engine, LoopbackProxyService proxy)
        : base(looper)
    {
        _queue = queue;
        _engine = engine;
        _proxy = proxy;
        _handler = new Handler(looper);

        _queue.PlaylistChanged += OnQueueChanged;
        _queue.PropertyChanged += OnQueuePropertyChanged;
    }

    // SimpleBasePlayer requires every access to happen on the Looper thread it was built with, but
    // PlayerQueueService's events (and the host service's own position-refresh tick) can fire from
    // whatever thread triggered the underlying change - the state-sync poll loop, a CommunityToolkit
    // native player callback, or a plain background Timer. Missing this causes an immediate
    // Java.Lang.IllegalStateException("Player is accessed on the wrong thread.") crash.
    //
    // Always posted, even from the looper thread itself, and never more than one at a time: a single
    // queue change raises a burst of events (a track change alone is four PropertyChanged plus
    // PlaylistChanged), and each used to rebuild the whole session state - playlist, metadata,
    // commands - on the spot. Posting lets the burst finish first and folds it into one GetState.
    private void PostInvalidate()
    {
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 1)
        {
            return;
        }

        _handler.Post(() =>
        {
            // Cleared before, not after, so a change raised while GetState runs still gets its own pass.
            Interlocked.Exchange(ref _invalidatePending, 0);
            InvalidateState();
        });
    }

    private void OnQueueChanged()
    {
        PostInvalidate();
    }

    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PostInvalidate();
    }

    /// <summary>
    /// Called from the host service's periodic tick to keep the notification's position/scrubber fresh.
    /// </summary>
    public void RefreshPosition()
    {
        PostInvalidate();
    }

    protected override State GetState()
    {
        var current = _queue.CurrentValue;

        // Player.Commands isn't just a set of user actions the notification/lock-screen widget can
        // invoke - it also gates which player state external controllers (including the legacy
        // MediaSessionCompat bridge system UI actually renders from) are even allowed to read. Without
        // these "Get" commands, session.Player.MediaMetadata/CurrentPosition/etc. read back correctly
        // in-process (we're not a "controller" to ourselves), but every external observer sees an
        // unset/placeholder state - which is exactly why the notification never got real artwork
        // (worked around separately) and the scrubber/position never appeared at all.
        // The binding's Builder-pattern methods (Add/Set/Build) are conservatively typed as returning
        // nullable references even though these Java "return this;" fluent APIs never actually return
        // null - the null-forgiving operators throughout this method suppress that false-positive
        // nullable-flow noise rather than reflecting any real nullability here.
        var commands = PlayerCommands.Empty!.BuildUpon()!
            .Add(InterfaceConsts.CommandPlayPause)!
            .Add(InterfaceConsts.CommandSeekBack)!
            .Add(InterfaceConsts.CommandSeekForward)!
            .Add(InterfaceConsts.CommandSeekInCurrentMediaItem)!
            .Add(InterfaceConsts.CommandGetCurrentMediaItem)!
            .Add(InterfaceConsts.CommandGetTimeline)!
            .Add(InterfaceConsts.CommandGetMetadata)!
            .Add(InterfaceConsts.CommandGetTracks)!;

        if (_queue.Count > 0)
        {
            commands.Add(InterfaceConsts.CommandSeekToNext)!
                    .Add(InterfaceConsts.CommandSeekToPrevious);
        }

        var builder = new State.Builder()
            .SetAvailableCommands(commands.Build()!)!
            .SetPlayWhenReady(_queue.IsPlaying, InterfaceConsts.PlayWhenReadyChangeReasonUserRequest)!
            .SetPlaybackState(current is null ? InterfaceConsts.StateIdle : InterfaceConsts.StateReady)!
            .SetContentPositionMs((long)_engine.Position.TotalMilliseconds)!;

        if (current is not null)
        {
            var metadataBuilder = new MediaMetadata.Builder()
                .SetTitle(current.Title)!
                .SetArtist(current.AllArtists)!
                .SetArtworkUri(global::Android.Net.Uri.Parse(_proxy.TrackCoverUrl(current.Id).ToString()))!;

            var mediaItem = new MediaItem.Builder()
                .SetMediaId(current.Id.ToString())!
                .SetMediaMetadata(metadataBuilder.Build()!)!
                .Build()!;

            var itemDataBuilder = new SimpleBasePlayer.MediaItemData.Builder(Java.Lang.Long.ValueOf(current.Id))
                .SetMediaItem(mediaItem)!
                .SetIsSeekable(true)!;

            var durationMs = (long)_engine.Duration.TotalMilliseconds;
            if (durationMs > 0)
            {
                itemDataBuilder.SetDurationUs(durationMs * 1000);
            }

            // Without this, the playlist has an item but nothing points at it (CurrentMediaItemIndex
            // stays at its default of -1), so the session reports no active item at all - no metadata,
            // no position, despite everything above being set correctly. This single call is what
            // actually makes the notification/lock-screen widget show title, artwork and the scrubber.
            builder.SetPlaylist([itemDataBuilder.Build()!])!
                   .SetCurrentMediaItemIndex(0);
        }

        return builder.Build()!;
    }

    protected override IListenableFuture? HandleSetPlayWhenReady(bool playWhenReady)
    {
        _queue.IsPlaying = playWhenReady;
        return ImmediateFuture();
    }

    protected override IListenableFuture? HandleSeek(int mediaItemIndex, long positionMs, int seekCommand)
    {
        switch (seekCommand)
        {
            case InterfaceConsts.CommandSeekToNext:
                _queue.Next();
                break;
            case InterfaceConsts.CommandSeekToPrevious:
                _queue.Previous();
                break;
            default:
                _ = _engine.SeekTo(TimeSpan.FromMilliseconds(positionMs), CancellationToken.None);
                PostInvalidate();
                break;
        }

        return ImmediateFuture();
    }

    protected override IListenableFuture? HandlePrepare()
    {
        return ImmediateFuture();
    }

    protected override IListenableFuture? HandleStop()
    {
        return ImmediateFuture();
    }

    protected override IListenableFuture? HandleRelease()
    {
        _queue.PlaylistChanged -= OnQueueChanged;
        _queue.PropertyChanged -= OnQueuePropertyChanged;
        return ImmediateFuture();
    }

    // SimpleBasePlayer's Handle* overrides all return a ListenableFuture that resolves once the
    // (potentially async) operation completes - every one of ours is already-synchronous C# state
    // updates, so this just wraps that as an already-resolved future via the standard AndroidX
    // adapter (Guava's own Futures.immediateVoidFuture() helper isn't present in these bindings).
    private static IListenableFuture? ImmediateFuture()
    {
        return CallbackToFutureAdapter.GetFuture(new ImmediateResolver());
    }

    private sealed class ImmediateResolver : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer? completer)
        {
            completer?.Set(null);
            return new Java.Lang.String("immediate");
        }
    }
}
