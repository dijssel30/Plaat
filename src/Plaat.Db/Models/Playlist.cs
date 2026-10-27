namespace Plaat.Db.Models;

public class Playlist
{
    public uint Id { get; init; }
    public required string Name { get; init; }
    public int UserId { get; init; }
    public User User { get; init; } = null!;
    public bool IsPinned { get; init; }
    public ICollection<PlaylistTrack> PlaylistTracks { get; init; } = [];
}
