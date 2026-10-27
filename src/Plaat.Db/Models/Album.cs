namespace Plaat.Db.Models;

public class Album
{
    public uint Id { get; init; }

    public ICollection<Artist> AlbumArtists { get; init; } = [];

    public required string Title { get; init; }

    public uint? CoverArtId { get; init; }

    public CoverArt? CoverArt { get; set; }

    public uint TotalTracks { get; init; }

    public uint TotalDiscs { get; init; }

    public ICollection<Track> Tracks { get; init; } = [];
}
