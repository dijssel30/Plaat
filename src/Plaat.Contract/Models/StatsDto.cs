namespace Plaat.Contract.Models;

/// <summary>
/// Listening stats for one <see cref="StatsRange"/> and <see cref="StatsScope"/>: the play history
/// inside that window and the top genres/artists by play count within it, alongside the library
/// totals (which are range- and scope-independent).
/// </summary>
public sealed class StatsDto
{
    /// <summary>
    /// Every play inside the requested range, as recorded - i.e. UTC. Callers that bucket these
    /// into days or hours want <see cref="DateTime.ToLocalTime"/> first.
    /// </summary>
    public IReadOnlyCollection<DateTime> PlayHistory { get; init; } = [];
    public IReadOnlyCollection<NamedCountDto> TopGenres { get; init; } = [];
    public IReadOnlyCollection<NamedCountDto> TopArtists { get; init; } = [];
    public required LibraryStatsDto Library { get; init; }
}
