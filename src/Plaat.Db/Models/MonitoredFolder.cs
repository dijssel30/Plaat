namespace Plaat.Db.Models;

public class MonitoredFolder
{
    public uint Id { get; init; }

    public required string Path { get; init; }

    public DateTimeOffset? LastScannedAt { get; set; }
}
