using JetBrains.Annotations;

namespace Plaat.Infrastructure.Discogs.Models;

[PublicAPI]
internal sealed class DiscogsArtistResponseJson
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Profile { get; init; }
    public DiscogsImageJson[]? Images { get; init; }
}
