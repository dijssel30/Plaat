using System.Threading.RateLimiting;

namespace Plaat.Infrastructure.Discogs;

/// <summary>
/// Throttles outgoing Discogs API calls to stay within Discogs' rate limit
/// (60 requests/minute authenticated, 25/minute unauthenticated).
/// </summary>
internal sealed class DiscogsRateLimiter(DiscogsOptions options) : IDisposable
{
    private readonly SlidingWindowRateLimiter _limiter = new(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(1, options.RequestsPerMinute),
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue
    });

    public async Task WaitForPermitAsync(CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(1, cancellationToken);

        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Could not acquire a Discogs API rate limit permit.");
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
