namespace Plaat.Contract.Models;

/// <summary>
/// Column a track listing can be ordered by. Mirrors the sort options in the track table UI.
/// </summary>
public enum TrackSortField
{
    Title,
    Artist,
    Album,
    Year,
    Plays
}
