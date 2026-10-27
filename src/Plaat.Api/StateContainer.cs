using Plaat.Contract.Models;

namespace Plaat.Api;

public sealed class StateContainer
{
    /// <summary>
    /// Guards every read-modify-write on this container. The same instance is shared by all
    /// concurrent requests of a user (multiple tabs/devices), so callers must hold it while
    /// touching <see cref="SecondsPlayed"/>, <see cref="PlayCounted"/> or the other members.
    /// Never hold it across an await.
    /// </summary>
    public Lock SyncRoot { get; } = new();

    public MusicState? State { get; set; }

    public uint CurrentTrack { get; set; }

    public List<int> SecondsPlayed { get; set; } = [];

    public bool PlayCounted { get; set; }
}
