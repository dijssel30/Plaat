using Plaat.Contract;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Album-header/track-list formatting that has no MAUI dependency, so it can be unit tested
/// without Android test infrastructure (see Plaat.Mobile.Tests.csproj).
/// </summary>
public static class AlbumTrackGrouping
{
    /// <summary>
    /// Splits disc/track-ordered tracks into per-disc groups. A single-disc album still gets one
    /// group, but with an empty name so the list's group header collapses away.
    /// </summary>
    public static List<TrackDiscGroup> BuildDiscGroups(IEnumerable<TrackDto> tracks)
    {
        var discs = tracks
            .GroupBy(t => t.DiscNumber)
            .OrderBy(g => g.Key)
            .ToList();

        return [.. discs
            .Select((g, i) => new TrackDiscGroup(
                // Tracks with no disc tag come through as disc 0; fall back to the group's
                // position so a partially-tagged album still reads "CD 1 / CD 2".
                discs.Count > 1 ? $"CD {(g.Key == 0 ? i + 1 : g.Key)}" : string.Empty,
                g))];
    }

    /// <summary>
    /// The album's release year, or a "first-last" range when its tracks disagree (compilations,
    /// or an album whose tracks carry their original recording years). Null when no track on the
    /// album has a year at all.
    /// </summary>
    public static string? BuildYearLabel(IEnumerable<TrackDto> tracks)
    {
        var years = tracks
            .Select(t => t.Year)
            .Where(y => y > 0)
            .ToList();

        if (years.Count == 0)
        {
            return null;
        }

        var first = years.Min();
        var last = years.Max();

        return first == last ? first.ToString() : $"{first}–{last}";
    }
}
