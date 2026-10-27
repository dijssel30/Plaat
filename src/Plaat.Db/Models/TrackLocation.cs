namespace Plaat.Db.Models;

public class TrackLocation
{
    public uint Id { get; init; }
    public required string Path { get; init; }
    public Track Track { get; init; } = null!;
    public uint TrackId { get; init; }
    public bool IsSupportedFormat { get; set; }
    public bool Default { get; set; }
}
