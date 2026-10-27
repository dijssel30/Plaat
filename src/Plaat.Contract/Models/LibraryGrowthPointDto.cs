namespace Plaat.Contract.Models;

/// <summary>
/// One month of the library's growth curve, from track added dates.
/// </summary>
public sealed class LibraryGrowthPointDto
{
    /// <summary>
    /// First day of the month this point covers.
    /// </summary>
    public required DateTime Month { get; init; }

    /// <summary>
    /// Tracks added during this month.
    /// </summary>
    public required int Added { get; init; }

    /// <summary>
    /// Tracks in the library at the end of this month, counting everything added before it.
    /// </summary>
    public required int TotalTracks { get; init; }
}
