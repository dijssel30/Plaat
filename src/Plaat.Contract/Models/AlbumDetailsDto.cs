namespace Plaat.Contract.Models;

/// <summary>
/// Full album details including its tracks.
/// </summary>
public sealed class AlbumDetailsDto : AlbumOnlyDto
{
    public IReadOnlyCollection<TrackDto> Tracks { get; init; } = [];
}

/// <summary>
/// Minimal album summary used in listings.
/// </summary>
public class AlbumOnlyDto
{
    public uint Id { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyCollection<ArtistOnlyDto> Artists { get; init; }
    public string AllArtists
    {
        get
        {
            return string.Join(", ", Artists.Select(a => a.Name));
        }
    }
}
