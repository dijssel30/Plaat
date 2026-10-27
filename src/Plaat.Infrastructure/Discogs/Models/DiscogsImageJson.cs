using JetBrains.Annotations;

namespace Plaat.Infrastructure.Discogs.Models;

[PublicAPI]
internal sealed class DiscogsImageJson
{
    public string? Type { get; init; }
    public string? Uri { get; init; }
}
