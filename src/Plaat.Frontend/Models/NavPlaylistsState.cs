using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Models;

public sealed partial class NavPlaylistsState(
    IPlaatClient client,
    ILogger<NavPlaylistsState> logger)
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _loaded;

    public IReadOnlyCollection<PlaylistOnlyDto> Playlists { get; private set; } = [];

    public event Func<Task>? OnPlaylistsChanged;

    public TrackDto? DraggedTrack { get; private set; }

    public event Func<Task>? OnDragStateChanged;

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded)
            {
                return;
            }

            await LoadAsync(ct);
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
        _loaded = true;

        Fire(OnPlaylistsChanged);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Playlists = await client.GetPlaylists(ct);
    }

    public void BeginDrag(TrackDto track)
    {
        DraggedTrack = track;

        Fire(OnDragStateChanged);
    }

    public void EndDrag()
    {
        if (DraggedTrack is null)
        {
            return;
        }

        DraggedTrack = null;

        Fire(OnDragStateChanged);
    }

    public async Task<bool> DropOnPlaylistAsync(PlaylistOnlyDto playlist, CancellationToken ct = default)
    {
        var track = DraggedTrack;
        EndDrag();

        if (track is null || playlist.IsSmart)
        {
            return false;
        }

        try
        {
            await client.AddToPlaylist(playlist.Id, track.Id, ct);
            return true;
        }
        catch (Exception ex)
        {
            LogAddToPlaylistFailed(logger, ex, track.Id, playlist.Id);
            return false;
        }
    }

    private void Fire(Func<Task>? evt)
    {
        var actions = evt?.GetInvocationList() ?? [];
        foreach (var action in actions)
        {
            InvokeSafely((Func<Task>)action);
        }
    }

    private void InvokeSafely(Func<Task> action)
    {
        Task task;
        try
        {
            task = action();
        }
        catch (Exception ex)
        {
            LogSubscriberFailed(logger, ex);
            return;
        }

        _ = task.ContinueWith(
            t => LogSubscriberFailed(logger, t.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    [LoggerMessage(LogLevel.Error, "Failed to add track {TrackId} to playlist {PlaylistId}")]
    private static partial void LogAddToPlaylistFailed(ILogger logger, Exception ex, uint trackId, uint playlistId);

    [LoggerMessage(LogLevel.Error, "Unhandled exception in NavPlaylistsState subscriber")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception? ex);
}
