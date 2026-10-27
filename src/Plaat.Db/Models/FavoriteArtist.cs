namespace Plaat.Db.Models;

public class FavoriteArtist
{
    public int UserId { get; init; }
    public User User { get; init; } = null!;
    public uint ArtistId { get; init; }
    public Artist Artist { get; init; } = null!;
    public DateTime AddedDate { get; init; }
}
