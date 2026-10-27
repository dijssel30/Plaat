namespace Plaat.Contract.Models;

/// <summary>
/// The user's current playback state: queue, current track and playback position.
/// </summary>
public sealed class MusicState
{
    public IReadOnlyCollection<uint> TrackIds { get; set; } = [];

    public required uint CurrentTrack { get; init; }

    public required double CurrentSecond { get; set; }

    public bool IsShuffled { get; set; }

    /// <summary>
    /// The unshuffled track order, so shuffle can be undone after a page refresh. Only set when
    /// <see cref="IsShuffled"/> is true.
    /// </summary>
    public IReadOnlyCollection<uint>? OriginalTrackIds { get; set; }
}
