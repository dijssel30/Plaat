using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Models;

/// <summary>
/// Circuit-wide access to the current user's star ratings. Reads go straight to the API, but writes
/// go through here so every component showing the same track's rating - the track details modal
/// that changed it and the player bar showing the same track underneath - stays in step without
/// either knowing about the other.
/// </summary>
public sealed partial class TrackRatingState(IPlaatClient client, ILogger<TrackRatingState> logger)
{
    /// <summary>
    /// Raised after a rating was persisted, with the track id and its new value.
    /// </summary>
    public event Func<uint, int, Task>? OnRatingChanged;

    public Task<int> GetRating(uint trackId, CancellationToken ct = default)
    {
        return client.GetTrackRating(trackId, ct);
    }

    /// <summary>
    /// Persists a rating and notifies subscribers. Returns false if the track no longer exists, in
    /// which case nothing is raised.
    /// </summary>
    public async Task<bool> SetRating(uint trackId, int stars, CancellationToken ct = default)
    {
        stars = Math.Clamp(stars, TrackRatingDto.MinStars, TrackRatingDto.MaxStars);

        if (!await client.SetTrackRating(trackId, stars, ct))
        {
            return false;
        }

        foreach (var subscriber in OnRatingChanged?.GetInvocationList() ?? [])
        {
            InvokeSafely((Func<uint, int, Task>)subscriber, trackId, stars);
        }

        return true;
    }

    private void InvokeSafely(Func<uint, int, Task> subscriber, uint trackId, int stars)
    {
        Task task;
        try
        {
            task = subscriber(trackId, stars);
        }
        catch (Exception ex)
        {
            LogSubscriberFailed(logger, ex, nameof(OnRatingChanged));
            return;
        }

        _ = task.ContinueWith(
            t => LogSubscriberFailed(logger, t.Exception, nameof(OnRatingChanged)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception in {Event} subscriber")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception? ex, string @event);
}
