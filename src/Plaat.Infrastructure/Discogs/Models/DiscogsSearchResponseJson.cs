using JetBrains.Annotations;

namespace Plaat.Infrastructure.Discogs.Models;

[PublicAPI]
internal sealed class DiscogsSearchResponseJson
{
    public DiscogsSearchResultJson[]? Results { get; init; }
}
