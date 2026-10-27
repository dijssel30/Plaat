namespace Plaat.Db.Models;

public class CoverArt
{
    public uint Id { get; init; }
    public required string Path { get; init; }
    public required string Hash { get; init; }
    public required string MimeType { get; init; }
    public ICollection<Album> Albums { get; init; } = [];
}
