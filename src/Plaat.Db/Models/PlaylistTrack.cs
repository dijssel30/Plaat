namespace Plaat.Db.Models;

public class PlaylistTrack
{
    public uint PlaylistId { get; init; }
    public Playlist Playlist { get; init; } = null!;
    public uint TrackId { get; init; }
    public Track Track { get; init; } = null!;
    public int Position { get; set; }
}
