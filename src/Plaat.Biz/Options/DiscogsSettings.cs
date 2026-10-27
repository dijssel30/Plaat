namespace Plaat.Biz.Options;

public sealed class DiscogsSettings
{
    /// <summary>
    /// Master on/off switch for the Discogs artist enrichment feature.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Discogs personal access token. When set, requests are authenticated (60 requests/minute).
    /// When null, requests are unauthenticated (25 requests/minute) - set <see cref="RequestsPerMinute"/> accordingly.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Requests per minute to allow against the Discogs API.
    /// </summary>
    public int RequestsPerMinute { get; init; } = 25;
}
