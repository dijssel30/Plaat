using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Plaat.Contract;

namespace Plaat.Mobile.Services.Player;

/// <summary>
/// Persists the last-known queue (already-resolved <see cref="TrackDto"/>s, current index and
/// position) to a small JSON file in app storage, so a cold start can rebuild the queue - and
/// render the mini player / now-playing screen - instantly from disk instead of waiting on
/// <see cref="PlaybackStateSyncService.RestoreAsync"/>'s round trip for the full track library
/// (GetTracks over the network, then an ID->track join). The network restore still runs afterwards
/// to reconcile with the backend-persisted state (the source of truth for cross-device resume).
/// </summary>
public sealed class QueueStateStore(ILogger<QueueStateStore> logger)
{
    private static readonly string QueueFilePath = Path.Combine(FileSystem.AppDataDirectory, "queue-state.json");

    // The playback position lives in its own tiny file: it's saved every few seconds while playing,
    // and folding it into the queue file meant re-serializing the whole queue (easily hundreds of
    // tracks) each time just to move one number.
    private static readonly string PositionFilePath = Path.Combine(FileSystem.AppDataDirectory, "queue-position.json");

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public Task<QueueSnapshot?> LoadAsync()
    {
        return Task.Run<QueueSnapshot?>(async () =>
    {
        QueueSnapshot? snapshot;
        try
        {
            snapshot = await ReadFileAsync(QueueFilePath, QueueStateJsonContext.Default.QueueSnapshot);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load cached queue state");
            return null;
        }

        if (snapshot is null)
        {
            return null;
        }

        try
        {
            var position = await ReadFileAsync(PositionFilePath, QueueStateJsonContext.Default.QueuePosition);

            // Only trusted for the track the queue file says was current - a position saved for a
            // track the queue has since moved on from belongs to that older track, not this one.
            if (position is not null
                && snapshot.CurrentIndex >= 0
                && snapshot.CurrentIndex < snapshot.Tracks.Count
                && snapshot.Tracks[snapshot.CurrentIndex].Id == position.TrackId)
            {
                snapshot.CurrentSecond = position.CurrentSecond;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load cached playback position; resuming from the queue's own");
        }

        return snapshot;
    });
    }

    /// <summary>
    /// Writes the whole queue. Only needed when the queue itself changed.
    /// </summary>
    public Task SaveAsync(QueueSnapshot snapshot)
    {
        var position = snapshot.CurrentIndex >= 0 && snapshot.CurrentIndex < snapshot.Tracks.Count
            ? new QueuePosition { TrackId = snapshot.Tracks[snapshot.CurrentIndex].Id, CurrentSecond = snapshot.CurrentSecond }
            : null;

        return WriteAsync(async () =>
        {
            await WriteFileAsync(QueueFilePath, snapshot, QueueStateJsonContext.Default.QueueSnapshot);

            // Kept in step with the queue file: the position file wins on load, so an older one left
            // behind for this same track would rewind the resume point.
            if (position is not null)
            {
                await WriteFileAsync(PositionFilePath, position, QueueStateJsonContext.Default.QueuePosition);
            }
        });
    }

    /// <summary>
    /// Writes just the playback position - cheap enough for the sync loop to call every few seconds.
    /// </summary>
    public Task SavePositionAsync(QueuePosition position)
    {
        return WriteAsync(() => WriteFileAsync(PositionFilePath, position, QueueStateJsonContext.Default.QueuePosition));
    }

    public void Delete()
    {
        try
        {
            File.Delete(QueueFilePath);
            File.Delete(PositionFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete cached queue state");
        }
    }

    private async Task WriteAsync(Func<Task> write)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Callers are usually on the UI thread (the sync loop, a queue change); serializing a long
            // queue is real work that shouldn't land on a frame.
            await Task.Run(write).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist queue state");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task WriteFileAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        // Write-then-rename, so a process killed mid-write leaves the previous file intact rather
        // than a truncated one that no longer loads.
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, typeInfo);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<T?> ReadFileAsync<T>(string path, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo);
    }
}

public sealed class QueueSnapshot
{
    public List<TrackDto> Tracks { get; set; } = [];

    /// <summary>
    /// The pre-shuffle order, when the cached queue was shuffled; otherwise null.
    /// </summary>
    public List<TrackDto>? OriginalOrder { get; set; }

    public int CurrentIndex { get; set; } = -1;

    public double CurrentSecond { get; set; }
}

public sealed class QueuePosition
{
    public uint TrackId { get; set; }

    public double CurrentSecond { get; set; }
}

/// <summary>
/// Source-generated serialization metadata, so reading the cached queue at startup doesn't first pay
/// for System.Text.Json building it by reflection. Default (not web) options, matching the files
/// earlier versions already wrote.
/// </summary>
[JsonSerializable(typeof(QueueSnapshot))]
[JsonSerializable(typeof(QueuePosition))]
internal sealed partial class QueueStateJsonContext : JsonSerializerContext;
