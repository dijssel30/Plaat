using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Stats : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private static readonly (StatsRange Range, string Label)[] RangeOptions =
    [
        (StatsRange.Day, "Day"),
        (StatsRange.Week, "Week"),
        (StatsRange.Month, "Month"),
        (StatsRange.Year, "Year"),
        (StatsRange.All, "All time")
    ];

    private static readonly (StatsScope Scope, string Label)[] ScopeOptions =
    [
        (StatsScope.Me, "Me"),
        (StatsScope.Everyone, "Everyone")
    ];

    /// <summary>
    /// How many rows each "top" breakdown shows before the tail is dropped.
    /// </summary>
    private const int TopN = 8;

    private StatsDto? _stats;
    private bool _loading;

    /// <summary>
    /// Everyone rather than Me by default: plays recorded before plays carried a user have no user
    /// at all, so Me would open on a page that looks emptier than the library really is.
    /// </summary>
    private StatsScope _scope = StatsScope.Everyone;

    /// <summary>
    /// All time by default: the range switch is there to narrow the page down, so opening it on a
    /// window would quietly hide every artist and genre that has not been played lately.
    /// </summary>
    private StatsRange _range = StatsRange.All;

    private string[] _playLabels = [];
    private int[] _playCounts = [];
    private int _playTotal;

    private string[] _hourLabels = [];
    private int[] _hourCounts = [];
    private string _peakNote = string.Empty;

    private string[] _growthLabels = [];
    private int[] _growthCounts = [];

    // Ranked by plays, inside the selected range and scope.
    private IReadOnlyList<NamedCountDto> _topGenres = [];
    private IReadOnlyList<NamedCountDto> _topArtists = [];

    // Ranked by how many tracks the library holds - collection make-up, not listening.
    private IReadOnlyList<NamedCountDto> _trackArtists = [];
    private IReadOnlyList<NamedCountDto> _trackGenres = [];
    private IReadOnlyList<NamedCountDto> _formats = [];

    protected override Task OnParametersSetAsync()
    {
        return Load();
    }

    private async Task SetRange(StatsRange range)
    {
        _range = range;

        await Load();
    }

    private async Task SetScope(StatsScope scope)
    {
        _scope = scope;

        await Load();
    }

    /// <summary>
    /// Range and scope are applied server-side, so every switch is a round trip rather than a
    /// re-slice of something already in hand. The panels stay on screen (dimmed) meanwhile.
    /// </summary>
    private async Task Load()
    {
        _loading = true;

        try
        {
            using var cts = new CancellationTokenSource(Timeouts.NormalTimeout);

            _stats = await PlaatClient.GetStats(_range, _scope, cts.Token);
        }
        finally
        {
            _loading = false;
        }

        // Plays are stored in UTC; every bucket on this page is a local-time one.
        var plays = _stats.PlayHistory
            .Select(p => DateTime.SpecifyKind(p, DateTimeKind.Utc).ToLocalTime())
            .ToArray();

        RebuildPlayChart(plays);
        RebuildHourChart(plays);
        RebuildGrowthChart(_stats.Library.Growth);

        _topGenres = TakeTop(_stats.TopGenres);
        _topArtists = TakeTop(_stats.TopArtists);
        _trackArtists = TakeTop(_stats.Library.ArtistBreakdown);
        _trackGenres = TakeTop(_stats.Library.GenreBreakdown);
        _formats = TakeTop(_stats.Library.FormatBreakdown);
    }

    private void RebuildPlayChart(IReadOnlyCollection<DateTime> plays)
    {
        _playTotal = plays.Count;

        if (plays.Count == 0)
        {
            _playLabels = [];
            _playCounts = [];

            return;
        }

        // Bucket widths follow the range the server filtered on, so the first bucket starts exactly
        // where StatsRanges.StartOf put the cutoff and no bar is half-filled.
        var (labels, counts) = _range switch
        {
            StatsRange.Day => PlayHistoryChartBuilder.BucketByHour(plays),
            StatsRange.Week => PlayHistoryChartBuilder.BucketByDay(plays, 7),
            StatsRange.Month => PlayHistoryChartBuilder.BucketByDay(plays, 30),
            StatsRange.Year => PlayHistoryChartBuilder.BucketByMonth(plays, 12),
            _ => PlayHistoryChartBuilder.BucketByMonth(plays, PlayHistoryChartBuilder.MonthSpan(plays.Min()))
        };

        _playLabels = labels;
        _playCounts = counts;
    }

    private void RebuildHourChart(IReadOnlyCollection<DateTime> plays)
    {
        if (plays.Count == 0)
        {
            _hourLabels = [];
            _hourCounts = [];
            _peakNote = string.Empty;

            return;
        }

        var (labels, counts) = PlayHistoryChartBuilder.BucketByHourOfDay(plays);

        _hourLabels = labels;
        _hourCounts = counts;

        var peak = Array.IndexOf(counts, counts.Max());
        _peakNote = $"busiest around {labels[peak]}";
    }

    private void RebuildGrowthChart(IReadOnlyCollection<LibraryGrowthPointDto> growth)
    {
        _growthLabels = [.. growth.Select(g => g.Month.ToString("MMM yyyy"))];
        _growthCounts = [.. growth.Select(g => g.TotalTracks)];
    }

    private static IReadOnlyList<NamedCountDto> TakeTop(IReadOnlyCollection<NamedCountDto> counts)
    {
        return counts.OrderByDescending(c => c.Count).Take(TopN).ToArray();
    }

    /// <summary>
    /// Names how much of a breakdown a "top N" list is actually showing.
    /// </summary>
    private static string Counted(int shown, int total, string singular, string plural)
    {
        return shown < total
            ? $"top {shown} of {total:N0}"
            : $"{total:N0} {(total == 1 ? singular : plural)}";
    }

    private string PlaysNote
    {
        get
        {
            return $"{_playTotal:N0} {(_playTotal == 1 ? "play" : "plays")} {RangeNote}";
        }
    }

    private string NoPlaysNote
    {
        get
        {
            return (_range, _scope) switch
            {
                (StatsRange.All, StatsScope.Me) => "No plays of yours recorded yet.",
                (StatsRange.All, _) => "No plays recorded yet.",
                (_, StatsScope.Me) => $"No plays of yours {RangeNote}.",
                _ => $"No plays {RangeNote}."
            };
        }
    }

    private string RangeNote
    {
        get
        {
            return _range switch
            {
                StatsRange.Day => "in the last 24 hours",
                StatsRange.Week => "in the last 7 days",
                StatsRange.Month => "in the last 30 days",
                StatsRange.Year => "in the last 12 months",
                _ => "all time"
            };
        }
    }

    private string GrowthNote
    {
        get
        {
            return _growthLabels.Length == 0 ? string.Empty : $"since {_growthLabels[0]}";
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var days = (int)duration.TotalDays;
        var hours = duration.Hours;

        return days > 0 ? $"{days}d {hours}h" : $"{hours}h {duration.Minutes}m";
    }
}
