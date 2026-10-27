using Plaat.Db.Models;

namespace Plaat.Biz.Scanners;

/// <summary>
/// Applies a play count coming from a file import (iTunes XML / custom CSV) to a track. The imported
/// count is stored as one dedicated <see cref="Play"/> (<see cref="Play.IsImported"/>) so a re-import
/// can raise that base weight later without touching plays registered by listening.
/// </summary>
internal static class ImportedPlayCount
{
    /// <summary>
    /// The track must have its <see cref="Track.Plays"/> loaded.
    /// </summary>
    /// <returns>Whether the track was changed.</returns>
    public static bool Apply(Track track, uint importedCount)
    {
        if (importedCount >= int.MaxValue)
        {
            return false;
        }

        var importedPlay = track.Plays.FirstOrDefault(p => p.IsImported);

        // Not imported before, played exactly once and only with weight-1 plays: leave it alone.
        if (importedPlay is null && track.PlayCount == 1 && track.Plays.All(p => p.Weight == 1))
        {
            return false;
        }

        var currentWeight = importedPlay?.Weight ?? 0;

        if (importedCount <= currentWeight)
        {
            return false;
        }

        if (importedPlay is null)
        {
            track.Plays.Add(new Play
            {
                Weight = (int)importedCount,
                IsImported = true,
                DateTime = DateTime.UtcNow
            });
        }
        else
        {
            importedPlay.Weight = (int)importedCount;
        }

        track.PlayCount += importedCount - (uint)currentWeight;

        return true;
    }
}
