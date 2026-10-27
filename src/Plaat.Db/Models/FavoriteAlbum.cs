namespace Plaat.Db.Models;

public class FavoriteAlbum
{
    public int UserId { get; init; }
    public User User { get; init; } = null!;
    public uint AlbumId { get; init; }
    public Album Album { get; init; } = null!;
    public DateTime AddedDate { get; init; }
}
