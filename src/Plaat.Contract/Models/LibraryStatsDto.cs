namespace Plaat.Contract.Models;

/// <summary>
/// Aggregate totals for the whole library.
/// </summary>
public sealed class LibraryStatsDto
{
    public required int TrackCount { get; init; }
    public required int AlbumCount { get; init; }
    public required int ArtistCount { get; init; }
    public required int PossiblyCorruptCount { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public IReadOnlyCollection<NamedCountDto> FormatBreakdown { get; init; } = [];

    /// <summary>
    /// How many tracks the library holds per artist. This counts what is on disk, not what has been
    /// listened to - see <see cref="StatsDto.TopArtists"/> for the play-count ranking.
    /// </summary>
    public IReadOnlyCollection<NamedCountDto> ArtistBreakdown { get; init; } = [];

    /// <summary>
    /// How many tracks the library holds per genre.
    /// </summary>
    public IReadOnlyCollection<NamedCountDto> GenreBreakdown { get; init; } = [];

    /// <summary>
    /// Month-by-month growth of the library, oldest first.
    /// </summary>
    public IReadOnlyCollection<LibraryGrowthPointDto> Growth { get; init; } = [];
}
