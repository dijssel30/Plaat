namespace Plaat.Db.Models;

public class ConvertQueueItem
{
    public uint Id { get; init; }
    public bool Working { get; set; }

    /// <summary>
    /// Number of failed conversion attempts so far. Items that reach the maximum are skipped.
    /// </summary>
    public int Attempts { get; set; }

    public uint TrackLocationId { get; init; }
    public TrackLocation TrackLocation { get; init; } = null!;
}
