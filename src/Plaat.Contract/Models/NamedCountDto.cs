namespace Plaat.Contract.Models;

/// <summary>
/// A named category (genre, artist, file format) with its play/track count.
/// </summary>
public sealed class NamedCountDto
{
    public required string Name { get; init; }
    public required int Count { get; init; }
}
