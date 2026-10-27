namespace Plaat.Contract.Models;

/// <summary>
/// Location and content type of album's cover art image.
/// </summary>
public sealed class CoverArtDto
{
    public required string Path { get; init; }
    public required string Hash { get; init; }
    public required string MimeType { get; init; }
}
