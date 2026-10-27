using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Mobile.Services.Player;

/// <summary>
/// Port of the state-sync polling loop and cold-start restore flow embedded in
/// Plaat.Frontend/Components/Player/PlayerBar.razor.cs (OnInitializedAsync/UpdateLoop/UpdateState).
/// Gives cross-device playback resume "for free" via the same backend-persisted MusicState the web
/// frontend already relies on.
/// </summary>
public sealed class PlaybackStateSyncService(
    IPlaatClient plaatClient,
    PlayerQueueService queue,
    PlaybackEngine playbackEngine,
    QueueStateStore queueStateStore,
    TokenStorageService tokenStorage,
    ILogger<PlaybackStateSyncService> logger)
{
    // Without an explicit budget these pushes inherit HttpClient's 100s default, and because the
    // loop awaits them inline one unlucky request stalls position reporting for over a minute -
    // long enough for a whole track to pass unreported.
    private static readonly TimeSpan StatePushTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueuePushTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PositionSaveInterval = TimeSpan.FromSeconds(3);

    private const int MaxLoopRestarts = 5;

    private PeriodicTimer? _timer;
    private DateTime _lastPositionSave = DateTime.MinValue;
    private bool _subscribedForSnapshots;

    // 1 while a structural flush is scheduled but hasn't run yet - see OnPlaylistChanged.
    private int _structuralFlushPending;

    // Set while the queue is being rebuilt from a saved snapshot / the backend, so the
    // SetPlayList it does doesn't immediately push the half-built queue straight back.
    private bool _restoring;

    /// <summary>
    /// Rebuilds the queue from the locally cached snapshot (see <see cref="QueueStateStore"/>).
    /// Runs before <see cref="RestoreAsync"/> at cold start so the mini player / now-playing screen
    /// render immediately from disk instead of waiting on the network round trip for the full
    /// track library. A no-op when there's no cache yet.
    /// </summary>
    public async Task RestoreFromCacheAsync()
    {
        if (!tokenStorage.IsAuthenticated)
        {
            return;
        }

        _restoring = true;
        try
        {
            var snapshot = await queueStateStore.LoadAsync();
            if (snapshot is null || snapshot.Tracks.Count == 0)
            {
                return;
            }

            queue.SetPlayList(snapshot.Tracks, snapshot.OriginalOrder);
            queue.CurrentIndex = snapshot.CurrentIndex >= 0 && snapshot.CurrentIndex < snapshot.Tracks.Count
                ? snapshot.CurrentIndex
                : -1;

            playbackEngine.ResumeAt(TimeSpan.FromSeconds(snapshot.CurrentSecond));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore playback state from cache");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Called once at app startup (after a successful login/session restore) to rebuild the queue and resume position.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!tokenStorage.IsAuthenticated)
        {
            return;
        }

        _restoring = true;
        try
        {
            var state = await plaatClient.GetCurrentState(cancellationToken);
            if (state is null)
            {
                return;
            }

            var neededIds = state.TrackIds
                .Concat(state.OriginalTrackIds ?? Enumerable.Empty<uint>())
                .Distinct()
                .ToArray();

            var tracksById = (await plaatClient.GetTracksByIds(neededIds, cancellationToken))
                .ToDictionary(t => t.Id);

            var playList = state.TrackIds
                .Select(tracksById.GetValueOrDefault)
                .OfType<TrackDto>()
                .ToList();

            List<TrackDto>? originalOrder = null;
            if (state is { IsShuffled: true, OriginalTrackIds: not null })
            {
                originalOrder = [.. state.OriginalTrackIds.Select(tracksById.GetValueOrDefault).OfType<TrackDto>()];
            }

            if (playList.Count == 0)
            {
                // The backend keeps this state in memory only, so an API restart leaves it holding
                // just the current track id (rebuilt by the next position push) with an empty queue.
                // Applying that would wipe the queue we just rebuilt from the local cache.
                return;
            }

            queue.SetPlayList(playList, originalOrder);
            queue.CurrentIndex = playList.Select(t => t.Id).ToList().IndexOf(state.CurrentTrack);

            playbackEngine.ResumeAt(TimeSpan.FromSeconds(state.CurrentSecond));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore playback state");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Starts the 500ms position/queue push loop. Safe to call multiple times.
    /// </summary>
    public void StartSyncLoop()
    {
        if (!_subscribedForSnapshots)
        {
            // Structural queue edits (add/remove/reorder/next/shuffle) don't wait for the next
            // loop tick to be cached - persist them right away so a hard kill loses at most the
            // current playback position, never the queue contents. The same edits are also the
            // only time the queue is pushed to the backend (the loop tick only pushes position).
            queue.PlaylistChanged += OnPlaylistChanged;
            _subscribedForSnapshots = true;
        }

        if (_timer is not null)
        {
            return;
        }

        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        _ = RunLoopSafely();
    }

    private void OnPlaylistChanged()
    {
        // Read now rather than when the flush below runs: by then the restore that raised this has
        // finished and cleared the flag, and the half-built queue would be pushed straight back.
        if (_restoring || Interlocked.Exchange(ref _structuralFlushPending, 1) == 1)
        {
            return;
        }

        _ = FlushStructuralChange();
    }

    /// <summary>
    /// One user action is usually several queue mutations in a row - "play this album" is SetPlayList
    /// then Play, and both raise PlaylistChanged - and each of them used to write the whole queue to
    /// disk and push every track id to the backend. Yielding first lets the rest of that synchronous
    /// burst land, so the whole action costs one write and one push, without opening a debounce window
    /// a hard kill could lose the queue in.
    /// </summary>
    private async Task FlushStructuralChange()
    {
        await Task.Yield();
        Interlocked.Exchange(ref _structuralFlushPending, 0);

        await Task.WhenAll(PersistQueue(), PushPlaylist());
    }

    /// <summary>
    /// Supervises the position loop. This loop is the only thing that reports playback to the
    /// backend, and audio keeps playing perfectly without it, so a loop that ends early is silent
    /// data loss - every remaining track of the session goes uncounted. A failing tick is absorbed
    /// below; this is the backstop for anything that still ends the loop.
    /// </summary>
    private async Task RunLoopSafely()
    {
        try
        {
            for (var attempt = 1; attempt <= MaxLoopRestarts; attempt++)
            {
                try
                {
                    await RunLoop();
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Playback state sync loop terminated unexpectedly; restarting ({Attempt} of {Max})",
                        attempt,
                        MaxLoopRestarts);
                }
            }

            logger.LogError(
                "Playback state sync loop gave up after {Max} restarts - playback is no longer being reported",
                MaxLoopRestarts);
        }
        finally
        {
            // Hand the timer back however this ends: StartSyncLoop bails out early while _timer is
            // still set, so without this a loop that stopped could never be started again for the
            // life of the process.
            var timer = _timer;
            _timer = null;
            timer?.Dispose();
        }
    }

    private async Task RunLoop()
    {
        while (_timer is not null && await _timer.WaitForNextTickAsync())
        {
            try
            {
                if (queue.CurrentIndex < 0 || !queue.IsPlaying)
                {
                    continue;
                }

                if (playbackEngine.HandleTrimEndIfReached())
                {
                    // This tick's position belonged to a track we've already left/stopped.
                    continue;
                }

                await PushTrackPlayback();
                await PersistPosition();
            }
            catch (Exception ex)
            {
                // One bad tick - a queue mutated mid-enumeration, a transient IO failure - costs
                // this sample only. It must not end reporting for the life of the process.
                logger.LogWarning(ex, "Playback state sync tick failed; continuing");
            }
        }
    }

    /// <summary>
    /// Pushes just the current track's playback position. Runs on every loop tick.
    /// </summary>
    private async Task PushTrackPlayback()
    {
        var current = queue.CurrentValue;
        if (current is null)
        {
            return;
        }

        var playback = new TrackPlaybackState
        {
            CurrentSecond = playbackEngine.Position.TotalSeconds,
            CurrentTrack = current.Id
        };

        using var timeout = new CancellationTokenSource(StatePushTimeout);

        try
        {
            await plaatClient.UpdateCurrentTrackPlayback(playback, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Dropped on purpose: the next tick is 500ms away, so waiting on a slow push only
            // costs us the samples we'd have taken meanwhile.
            logger.LogDebug("Playback position push timed out after {Timeout}", StatePushTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update current playback position");
        }
    }

    /// <summary>
    /// Pushes the play queue. Runs only on a structural queue change, never on the loop tick.
    /// </summary>
    private async Task PushPlaylist()
    {
        if (_restoring || queue.Count == 0)
        {
            return;
        }

        // IsShuffled is itself "OriginalOrder is not null", so reading the two separately leaves a
        // window for the queue to be unshuffled from the UI thread in between - which is what the
        // null-forgiving operator that used to sit here would have thrown on. Read it once.
        var originalOrder = queue.IsShuffled ? queue.OriginalOrder : null;

        var playlist = new PlaylistState
        {
            CurrentTrack = queue.CurrentValue?.Id ?? 0,
            TrackIds = [.. queue.Select(t => t.Id)],
            IsShuffled = queue.IsShuffled,
            OriginalTrackIds = originalOrder is null ? null : [.. originalOrder.Select(t => t.Id)]
        };

        using var timeout = new CancellationTokenSource(QueuePushTimeout);

        try
        {
            await plaatClient.UpdateCurrentPlaylist(playlist, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Play queue push timed out after {Timeout}", QueuePushTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update current playlist state");
        }
    }

    /// <summary>
    /// Writes the whole queue (plus position) to the local cache. Only called for structural queue
    /// changes that must not be lost; the loop tick uses the much cheaper <see cref="PersistPosition"/>.
    /// </summary>
    private Task PersistQueue()
    {
        if (queue.CurrentIndex < 0 || queue.Count == 0)
        {
            return Task.CompletedTask;
        }

        _lastPositionSave = DateTime.UtcNow;

        var originalOrder = queue.IsShuffled ? queue.OriginalOrder : null;

        // The copies are taken here, on the caller's thread; the store serializes them off it.
        return queueStateStore.SaveAsync(new QueueSnapshot
        {
            Tracks = [.. queue],
            OriginalOrder = originalOrder is null ? null : [.. originalOrder],
            CurrentIndex = queue.CurrentIndex,
            CurrentSecond = playbackEngine.Position.TotalSeconds
        });
    }

    /// <summary>
    /// Writes just the playback position to the local cache, throttled - the loop calls this every 500ms.
    /// </summary>
    private Task PersistPosition()
    {
        if (queue.CurrentValue is not { } current || DateTime.UtcNow - _lastPositionSave < PositionSaveInterval)
        {
            return Task.CompletedTask;
        }

        _lastPositionSave = DateTime.UtcNow;

        return queueStateStore.SavePositionAsync(new QueuePosition
        {
            TrackId = current.Id,
            CurrentSecond = playbackEngine.Position.TotalSeconds
        });
    }

    public async Task ClearAsync()
    {
        queueStateStore.Delete();

        try
        {
            await plaatClient.ClearCurrentState(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clear current playback state");
        }
    }
}
