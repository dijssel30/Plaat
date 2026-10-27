namespace Plaat.Contract.Models;

/// <summary>
/// Time window a listening-stats request covers.
/// </summary>
public enum StatsRange
{
    Day,
    Week,
    Month,
    Year,
    All
}

public static class StatsRanges
{
    /// <summary>
    /// Inclusive local-time start of <paramref name="range"/> relative to <paramref name="now"/>,
    /// or null for all time. Lives in the contract because both sides need it and must agree: the
    /// API filters plays from here, and the frontend draws its first chart bucket here. If the two
    /// drifted apart the leading bucket would silently show a partial count.
    /// </summary>
    public static DateTime? StartOf(StatsRange range, DateTime now)
    {
        return range switch
        {
            // The last 24 hours, by hour; the last 7/30 days and the last 12 months, whole buckets.
            StatsRange.Day => now.Date.AddHours(now.Hour).AddHours(-23),
            StatsRange.Week => now.Date.AddDays(-6),
            StatsRange.Month => now.Date.AddDays(-29),
            StatsRange.Year => new DateTime(now.Year, now.Month, 1, 0, 0, 0, now.Kind).AddMonths(-11),
            _ => null
        };
    }
}
