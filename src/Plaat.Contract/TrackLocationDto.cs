namespace Plaat.Contract;

/// <summary>
/// A single filesystem location a track's audio file can be found at.
/// </summary>
public sealed class TrackLocationDto
{
    public required uint Id { get; init; }
    public required string Path { get; init; }
    public required bool IsSupportedFormat { get; init; }
    public required bool IsDefault { get; init; }
}
