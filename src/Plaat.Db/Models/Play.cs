namespace Plaat.Db.Models;

public class Play
{
    public uint Id { get; init; }
    public Track Track { get; init; } = null!;
    public uint TrackId { get; init; }
    public int Weight { get; set; } = 1;

    /// <summary>
    /// True when this play is the base play count imported from a file (iTunes XML / custom CSV),
    /// as opposed to a play registered by actually listening.
    /// </summary>
    public bool IsImported { get; init; }

    public required DateTime DateTime { get; init; }
    public int? UserId { get; init; }
    public User? User { get; init; }
}
