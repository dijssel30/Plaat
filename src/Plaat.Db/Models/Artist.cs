namespace Plaat.Db.Models;

public class Artist
{
    public uint Id { get; init; }

    public required string Name { get; init; }

    public required string NormalizedName { get; init; }

    public int? DiscogsArtistId { get; init; }

    /// <summary>
    /// Artist bio from Discogs, cleaned of Discogs markup and stored as sanitized, ready-to-render
    /// HTML (see <c>DiscogsProfileFormatter</c>) - safe to render directly, e.g. via MarkupString.
    /// </summary>
    public string? DiscogsSummary { get; init; }

    /// <summary>
    /// Filename (relative to <c>PlaatOptions.ImagesStoragePath</c>) of the artist image downloaded from Discogs.
    /// </summary>
    public string? DiscogsImagePath { get; init; }

    public ArtistDiscogsLookupStatus DiscogsLookupStatus { get; init; } = ArtistDiscogsLookupStatus.NotAttempted;

    public ICollection<Track> Tracks { get; init; } = [];

    public ICollection<Album> Albums { get; init; } = [];
}
