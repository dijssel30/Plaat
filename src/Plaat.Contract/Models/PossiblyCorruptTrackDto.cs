namespace Plaat.Contract.Models;

public sealed record PossiblyCorruptTrackDto(
    uint Id,
    string Title,
    string AllArtists,
    string Album,
    uint AlbumId,
    IReadOnlyCollection<string> Paths);
