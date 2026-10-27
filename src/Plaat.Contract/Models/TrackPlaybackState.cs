namespace Plaat.Contract.Models;

/// <summary>
/// A lightweight playback-position ping: which track is playing and how many seconds in.
/// Sent frequently (every player timer tick) and deliberately carries no queue data - the queue is
/// pushed separately via <see cref="PlaylistState"/> only when it actually changes.
/// </summary>
public sealed class TrackPlaybackState
{
    public required uint CurrentTrack { get; init; }

    public required double CurrentSecond { get; init; }
}
