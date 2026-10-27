namespace Plaat.Db.Models;

public class Genre
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public ICollection<Track> Tracks { get; init; } = [];
}
