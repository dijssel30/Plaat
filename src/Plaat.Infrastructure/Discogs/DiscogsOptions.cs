namespace Plaat.Infrastructure.Discogs;

public sealed class DiscogsOptions
{
    /// <summary>
    /// Personal access token used to authenticate against the Discogs API. When set, requests
    /// are authenticated (60 requests/minute). When null, requests are unauthenticated (25 requests/minute).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Requests per minute to allow against the Discogs API.
    /// </summary>
    public int RequestsPerMinute { get; init; } = 25;
}
