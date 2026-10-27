using Microsoft.Extensions.Logging;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Workers;

public partial class SmartPlaylistCountService(
    IPlaylistRepository playlistRepository,
    ILogger<SmartPlaylistCountService> logger)
{
    private int _busy;

    /// <summary>
    /// Whether a recalculation run is currently in progress on the background processing loop.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            return Volatile.Read(ref _busy) != 0;
        }
    }

    /// <summary>
    /// Recalculates and persists every smart playlist's cached track count, if a run isn't already
    /// in progress. A tick that finds a run already in progress is simply skipped.
    /// </summary>
    public async Task RecalculateAll(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await playlistRepository.RecalculateAllSmartPlaylistTrackCounts(cancellationToken);
        }
        catch (Exception ex)
        {
            LogRecalculateFailed(ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to recalculate smart playlist track counts")]
    private partial void LogRecalculateFailed(Exception ex);
}
