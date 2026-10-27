using JetBrains.Annotations;

namespace Plaat.Infrastructure.Discogs.Models;

[PublicAPI]
internal sealed class DiscogsSearchResultJson
{
    public int Id { get; init; }
    public string? Title { get; init; }
    public string? Type { get; init; }
}
