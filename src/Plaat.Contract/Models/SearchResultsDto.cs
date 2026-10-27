namespace Plaat.Contract.Models;

/// <summary>
/// Combined, capped results of a global library search. Each section is limited server-side so a
/// keystroke transfers ~kilobytes instead of the whole catalog.
/// </summary>
public sealed class SearchResultsDto
{
    public IReadOnlyList<TrackDto> Tracks { get; init; } = [];
    public IReadOnlyList<AlbumOnlyDto> Albums { get; init; } = [];
    public IReadOnlyList<ArtistOnlyDto> Artists { get; init; } = [];
}
