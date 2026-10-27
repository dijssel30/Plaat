namespace Plaat.Biz.Options;

public sealed class RateLimitSettings
{
    /// <summary>
    /// Master on/off switch for rate limiting the authentication endpoints.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Length of the window the limits below are counted in, in seconds.
    /// </summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum requests to <c>/api/auth/login</c> and <c>/api/auth/token</c> per client IP per window.
    /// </summary>
    public int LoginPermitLimit { get; init; } = 3;

    /// <summary>
    /// Maximum requests to <c>/api/auth/refresh</c> per client IP per window. Higher than the login
    /// limit because refreshes happen automatically, and Plaat.Frontend's refresh calls all come
    /// from the frontend's own IP.
    /// </summary>
    public int RefreshPermitLimit { get; init; } = 30;

    /// <summary>
    /// IP addresses whose <c>X-Forwarded-For</c> header is trusted to carry the real client IP -
    /// i.e. where Plaat.Frontend runs. Any other caller's header is ignored, so the limit can't be
    /// dodged by spoofing it.
    /// </summary>
    public string[] TrustedProxies { get; init; } = ["127.0.0.1", "::1"];
}
