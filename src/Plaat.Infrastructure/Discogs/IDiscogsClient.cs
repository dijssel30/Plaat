namespace Plaat.Infrastructure.Discogs;

/// <summary>
/// Client for looking up artist metadata (bio, image, cross-references) from the Discogs API.
/// </summary>
public interface IDiscogsClient
{
    /// <summary>
    /// Searches Discogs for an artist whose name matches <paramref name="name"/> exactly
    /// (trimmed, case-insensitive, ignoring Discogs's own "(n)" disambiguation suffix for
    /// same-named artists) - so "MUSE" still matches "Muse", and "Sam Smith" still matches
    /// "Sam Smith (12)", but not "Muse (band)" or any other near-match. When multiple artists
    /// share the exact name, the first (most popular, per Discogs's result ordering) is
    /// returned. Returns null if no exact match is found.
    /// </summary>
    Task<DiscogsSearchArtist?> SearchArtistByExactName(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches full artist details (profile text and primary image) by Discogs artist ID.
    /// Returns null if the artist does not exist.
    /// </summary>
    Task<DiscogsArtistDetails?> GetArtistById(int discogsArtistId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the display name/title of an entity referenced by ID in Discogs profile markup
    /// (e.g. the "3824954" in "[a3824954]"). <paramref name="referenceType"/> is the markup type
    /// letter: 'a' (artist), 'l' (label), 'm' (master release) or 'r' (release).
    /// Returns null if the entity does not exist or the type is unrecognized.
    /// </summary>
    Task<string?> ResolveReferenceName(char referenceType, int discogsId, CancellationToken cancellationToken);
}

public sealed record DiscogsSearchArtist(int Id, string Title);

public sealed record DiscogsArtistDetails(int Id, string Name, string? Profile, string? ImageUri);
