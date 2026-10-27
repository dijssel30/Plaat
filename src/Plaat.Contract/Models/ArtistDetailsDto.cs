namespace Plaat.Contract.Models;

/// <summary>
/// Full artist details including their tracks, albums and Discogs enrichment data.
/// </summary>
public sealed class ArtistDetailsDto : ArtistOnlyDto
{
    public IReadOnlyCollection<TrackDto> Tracks { get; init; } = [];
    public IReadOnlyCollection<AlbumOnlyDto> Albums { get; init; } = [];

    /// <summary>
    /// Sanitized, ready-to-render HTML (e.g. via MarkupString) - not plain text.
    /// </summary>
    public string? DiscogsSummary { get; init; }
    public bool HasDiscogsImage { get; init; }
    public required ArtistDiscogsLookupStatus DiscogsLookupStatus { get; init; }
}

/// <summary>
/// Minimal artist summary used in listings.
/// </summary>
public class ArtistOnlyDto
{
    public uint Id { get; init; }
    public required string Name { get; init; }
}
