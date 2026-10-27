using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for listening and collection statistics.
/// </summary>
public interface IStatsRepository
{
    /// <summary>
    /// Gets the play history and the top genres/artists by play count inside
    /// <paramref name="range"/>, counting only <paramref name="userId"/>'s plays (or everyone's
    /// when it is null), plus the library totals.
    /// </summary>
    Task<StatsDto> GetStats(StatsRange range, int? userId, CancellationToken ct);
}

internal sealed class StatsRepository(
    IDbContextFactory<PlaatContext> contextFactory,
    ILibraryCache cache) : IStatsRepository
{
    /// <summary>
    /// How far back the growth curve is drawn month by month. Everything older is folded into the
    /// first point's running total, so the curve still starts at the right height but a library
    /// carrying decades-old added dates (imported from a CSV, say) cannot produce hundreds of bars.
    /// </summary>
    private const int MaxGrowthMonths = 120;

    public async Task<StatsDto> GetStats(StatsRange range, int? userId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Plays are stored in UTC; the range boundaries are local (the chart buckets are local
        // days/hours), so the cutoff crosses over here rather than anywhere near the query.
        var cutoff = StatsRanges.StartOf(range, DateTime.Now)?.ToUniversalTime();

        var plays = context.Plays.AsQueryable();

        if (cutoff is not null)
        {
            plays = plays.Where(p => p.DateTime >= cutoff);
        }

        if (userId is not null)
        {
            plays = plays.Where(p => p.UserId == userId);
        }

        // Deliberately uncached: the result depends on the range and the caller, and a range like
        // "last 7 days" goes stale with the clock rather than with the data, which is exactly what
        // ILibraryCache (no expiry, invalidation by hand) is bad at. These are three grouped
        // queries over one table - the genuinely expensive part is the library totals, and those
        // stay cached below.
        var playHistory = await plays
            .Select(p => p.DateTime)
            .ToArrayAsync(ct);

        var genreCounts = await plays
            .SelectMany(p => p.Track.Genres, (_, g) => g.Name)
            .GroupBy(name => name)
            .Select(g => new NamedCountDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArrayAsync(ct);

        var artistCounts = await plays
            .SelectMany(p => p.Track.Artists, (_, a) => a.Name)
            .GroupBy(name => name)
            .Select(g => new NamedCountDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArrayAsync(ct);

        return new StatsDto
        {
            PlayHistory = playHistory,
            TopGenres = genreCounts,
            TopArtists = artistCounts,
            Library = await cache.GetOrCreateAsync(CacheKeys.LibraryStats, LibraryStatsFactory, ct)
        };
    }

    private async ValueTask<LibraryStatsDto> LibraryStatsFactory(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var formatCounts = await context.Tracks
            .GroupBy(t => t.Format ?? "Unknown")
            .Select(g => new NamedCountDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArrayAsync(ct);

        // Deliberately over Tracks, not Plays: how much of the collection an artist/genre accounts
        // for, whether or not any of it has ever been played.
        var artistTrackCounts = await context.Tracks
            .SelectMany(t => t.Artists, (_, a) => a.Name)
            .GroupBy(name => name)
            .Select(g => new NamedCountDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArrayAsync(ct);

        var genreTrackCounts = await context.Tracks
            .SelectMany(t => t.Genres, (_, g) => g.Name)
            .GroupBy(name => name)
            .Select(g => new NamedCountDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArrayAsync(ct);

        var trackCount = await context.Tracks.CountAsync(ct);
        var albumCount = await context.Albums.CountAsync(ct);
        var artistCount = await context.Artists.CountAsync(ct);
        var possiblyCorruptCount = await context.Tracks.CountAsync(t => t.PossiblyCorrupt, ct);

        var durations = await context.Tracks.Select(t => t.Duration).ToArrayAsync(ct);
        var totalDuration = durations.Aggregate(TimeSpan.Zero, (a, b) => a + b);

        var addedPerMonth = await context.Tracks
            .GroupBy(t => new { t.AddedDate.Year, t.AddedDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToArrayAsync(ct);

        return new LibraryStatsDto
        {
            TrackCount = trackCount,
            AlbumCount = albumCount,
            ArtistCount = artistCount,
            PossiblyCorruptCount = possiblyCorruptCount,
            TotalDuration = totalDuration,
            FormatBreakdown = formatCounts,
            ArtistBreakdown = artistTrackCounts,
            GenreBreakdown = genreTrackCounts,
            Growth = BuildGrowth(addedPerMonth.Select(a => (new DateTime(a.Year, a.Month, 1), a.Count)))
        };
    }

    /// <summary>
    /// Turns "n tracks were added in month m" into a gapless running total per month, up to the
    /// current month. Months where nothing was added still get a point - a flat stretch in the
    /// curve is information, and leaving it out would compress an idle year into one bar's width.
    /// </summary>
    private static LibraryGrowthPointDto[] BuildGrowth(IEnumerable<(DateTime Month, int Added)> addedPerMonth)
    {
        var added = addedPerMonth.ToDictionary(a => a.Month, a => a.Added);

        if (added.Count == 0)
        {
            return [];
        }

        var today = DateTime.Today;
        var lastMonth = new DateTime(today.Year, today.Month, 1);
        var firstMonth = added.Keys.Min();

        if (firstMonth > lastMonth)
        {
            // Added dates in the future (a bad tag, a clock skew) - just end the curve at them.
            lastMonth = added.Keys.Max();
        }

        var cutoff = lastMonth.AddMonths(-(MaxGrowthMonths - 1));
        var carried = 0;

        if (firstMonth < cutoff)
        {
            carried = added.Where(a => a.Key < cutoff).Sum(a => a.Value);
            firstMonth = cutoff;
        }

        var points = new List<LibraryGrowthPointDto>();
        var total = carried;

        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            var inMonth = added.GetValueOrDefault(month);
            total += inMonth;

            points.Add(new LibraryGrowthPointDto
            {
                Month = month,
                Added = inMonth,
                TotalTracks = total
            });
        }

        return [.. points];
    }
}
