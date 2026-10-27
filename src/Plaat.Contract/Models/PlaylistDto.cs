namespace Plaat.Contract.Models;

/// <summary>
/// Full playlist details including its tracks.
/// </summary>
public class PlaylistDetailsDto : PlaylistOnlyDto
{
    public IReadOnlyCollection<TrackDto> Tracks { get; init; } = [];
}

/// <summary>
/// Minimal playlist summary used in listings.
/// </summary>
public class PlaylistOnlyDto
{
    public uint Id { get; init; }
    public required bool IsSmart { get; init; }
    public required string PlaylistName { get; init; }
    public required int TrackCount { get; init; }
    public required bool IsPinned { get; init; }
}

public sealed record SetPlaylistPinnedRequest(bool IsSmart, bool IsPinned);

public sealed record DuplicatePlaylistRequest(bool IsSmart, string NewName);
