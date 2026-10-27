using Microsoft.AspNetCore.Components;
using MudBlazor;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components;

public partial class TrackDetailsModal
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required TrackRatingState TrackRatings { get; init; }

    [Inject]
    public required ISnackbar Snackbar { get; init; }

    [Inject]
    public required ILogger<TrackDetailsModal> Logger { get; init; }

    [Parameter]
    public uint? TrackId { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    private enum PlayHistoryRange
    {
        Day,
        Month,
        Year,
        All
    }

    private TrackDetailsDto? _details;
    private int _rating;
    private bool _savingRating;
    private uint? _settingDefaultLocationId;
    private string _trimStartText = "";
    private string _trimEndText = "";
    private bool _savingTrim;
    private string? _trimError;
    private PlayHistoryRange _range = PlayHistoryRange.Month;
    private List<ChartSeries<int>> _series = [];
    private string[] _labels = [];

    protected override async Task OnParametersSetAsync()
    {
        if (TrackId is null)
        {
            _details = null;
            return;
        }

        if (_details?.Id == TrackId)
        {
            return;
        }

        await LoadDetails(TrackId.Value);
    }

    private async Task LoadDetails(uint trackId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _details = await PlaatClient.GetTrackDetails(trackId, cts.Token);

        _trimStartText = _details?.TrimStart is { } start ? FormatMinutesSeconds(start) : "";
        _trimEndText = _details?.TrimEnd is { } end ? FormatMinutesSeconds(end) : "";
        _trimError = null;

        RebuildChart();

        await LoadRating(trackId);
    }

    /// <summary>
    /// Ratings are per user, so they are not part of the (process-wide cached) track details and
    /// are fetched separately.
    /// </summary>
    private async Task LoadRating(uint trackId)
    {
        _rating = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            _rating = await TrackRatings.GetRating(trackId, cts.Token);
        }
        catch (Exception ex)
        {
            LogLoadRatingFailed(Logger, ex, trackId);
        }
    }

    private async Task SetRating(int stars)
    {
        if (_details is null || _savingRating)
        {
            return;
        }

        var previous = _rating;

        // Optimistic: the stars follow the click straight away and only roll back if the save fails.
        _rating = stars;
        _savingRating = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            if (!await TrackRatings.SetRating(_details.Id, stars, cts.Token))
            {
                _rating = previous;
                Snackbar.Add("Couldn't save the rating: this track no longer exists.", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            _rating = previous;
            LogSaveRatingFailed(Logger, ex, _details.Id);
            Snackbar.Add("Couldn't save the rating.", Severity.Error);
        }
        finally
        {
            _savingRating = false;
        }
    }

    private async Task SetDefault(uint locationId)
    {
        if (_details is null)
        {
            return;
        }

        _settingDefaultLocationId = locationId;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await PlaatClient.SetDefaultTrackLocation(_details.Id, locationId, cts.Token);
            await LoadDetails(_details.Id);
        }
        finally
        {
            _settingDefaultLocationId = null;
        }
    }

    private async Task Close()
    {
        _details = null;
        _rating = 0;
        _trimStartText = "";
        _trimEndText = "";
        _trimError = null;
        await OnClose.InvokeAsync();
    }

    private async Task SaveTrim()
    {
        if (_details is null || _savingTrim)
        {
            return;
        }

        if (!TryParseTrim(_trimStartText, out var start) || !TryParseTrim(_trimEndText, out var end))
        {
            _trimError = "Enter times as mm:ss.";
            return;
        }

        if (start is not null && end is not null && start >= end)
        {
            _trimError = "Start must be before end.";
            return;
        }

        _trimError = null;
        _savingTrim = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            if (!await PlaatClient.SetTrackTrim(_details.Id, start, end, cts.Token))
            {
                Snackbar.Add("Couldn't save the trim points: this track no longer exists.", Severity.Error);
                return;
            }

            await LoadDetails(_details.Id);
        }
        catch (Exception ex)
        {
            LogSaveTrimFailed(Logger, ex, _details.Id);
            Snackbar.Add("Couldn't save the trim points.", Severity.Error);
        }
        finally
        {
            _savingTrim = false;
        }
    }

    private async Task ClearTrim()
    {
        _trimStartText = "";
        _trimEndText = "";
        await SaveTrim();
    }

    /// <summary>
    /// Total minutes rather than the minutes component, so a point past the hour in a long mix
    /// reads (and round-trips through <see cref="TryParseTrim"/>) as "75:30" instead of "15:30".
    /// </summary>
    private static string FormatMinutesSeconds(TimeSpan value)
    {
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }

    private static bool TryParseTrim(string text, out TimeSpan? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split(':');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var minutes)
            && int.TryParse(parts[1], out var seconds)
            && minutes >= 0
            && seconds is >= 0 and < 60)
        {
            value = new TimeSpan(0, minutes, seconds);
            return true;
        }

        return false;
    }

    private static string FormatSampleRate(int sampleRateHz)
    {
        return $"{sampleRateHz / 1000.0:0.#} kHz";
    }

    private static string FormatLoudness(TrackDetailsDto details)
    {
        return details.IntegratedLoudnessLufs is { } lufs
            ? $"{lufs:0.#} LUFS (×{details.AttenuationFactor:0.##})"
            : "Not analyzed";
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.##} MB",
            >= 1024 => $"{bytes / 1024.0:0.##} KB",
            _ => $"{bytes} B"
        };
    }

    private void RebuildChart()
    {
        if (_details is null || _details.PlayHistory.Count == 0)
        {
            _series = [];
            _labels = [];
            return;
        }

        var (labels, counts) = _range switch
        {
            PlayHistoryRange.Day => PlayHistoryChartBuilder.BucketByHour(_details.PlayHistory),
            PlayHistoryRange.Month => PlayHistoryChartBuilder.BucketByDay(_details.PlayHistory, 30),
            PlayHistoryRange.Year => PlayHistoryChartBuilder.BucketByMonth(_details.PlayHistory, 12),
            _ => PlayHistoryChartBuilder.BucketByMonth(_details.PlayHistory, PlayHistoryChartBuilder.MonthSpan(_details.PlayHistory.Min()))
        };

        _labels = labels;
        _series = [new ChartSeries<int> { Name = "Plays", Data = counts }];
    }

    [LoggerMessage(LogLevel.Warning, "Failed to load the rating for track {TrackId}")]
    private static partial void LogLoadRatingFailed(ILogger logger, Exception ex, uint trackId);

    [LoggerMessage(LogLevel.Warning, "Failed to save the rating for track {TrackId}")]
    private static partial void LogSaveRatingFailed(ILogger logger, Exception ex, uint trackId);

    [LoggerMessage(LogLevel.Warning, "Failed to save trim points for track {TrackId}")]
    private static partial void LogSaveTrimFailed(ILogger logger, Exception ex, uint trackId);
}
