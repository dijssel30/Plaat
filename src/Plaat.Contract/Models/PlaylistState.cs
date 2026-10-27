namespace Plaat.Contract.Models;

/// <summary>
/// The user's play queue. Sent only when the queue itself changes - tracks added/removed/reordered,
/// shuffle toggled, or the current-track pointer moved - never on the playback-position timer.
/// </summary>
public sealed class PlaylistState
{
    public IReadOnlyCollection<uint> TrackIds { get; set; } = [];

    public required uint CurrentTrack { get; init; }

    public bool IsShuffled { get; set; }

    /// <summary>
    /// The unshuffled track order, so shuffle can be undone after a page refresh. Only set when
    /// <see cref="IsShuffled"/> is true.
    /// </summary>
    public IReadOnlyCollection<uint>? OriginalTrackIds { get; set; }
}
