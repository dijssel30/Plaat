namespace Plaat.Contract.Models;

/// <summary>
/// Recently added standalone tracks and albums, grouped separately.
/// </summary>
public sealed class RecentlyAddedDto
{
    public IReadOnlyCollection<TrackDto> Tracks { get; init; } = [];
    public IReadOnlyCollection<AlbumOnlyDto> Albums { get; init; } = [];
}
