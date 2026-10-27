namespace Plaat.Contract;

/// <summary>
/// Full track details: file/format metadata, playback locations and play history.
/// </summary>
public sealed class TrackDetailsDto : TrackDto
{
    public required DateTime AddedDate { get; init; }
    public required bool PossiblyCorrupt { get; init; }
    public required int BitRate { get; init; }
    public required int SampleRate { get; init; }
    public required int BitsPerSample { get; init; }
    public required string FileHash { get; init; }
    public required long FileSizeBytes { get; init; }
    public string? OriginalContentHash { get; init; }
    public required string MimeType { get; init; }
    public string? Format { get; init; }
    public string? Lyrics { get; init; }
    public required IReadOnlyCollection<TrackLocationDto> Locations { get; init; } = [];
    public required IReadOnlyCollection<DateTime> PlayHistory { get; init; } = [];
}
