using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Workers;

/// <summary>
/// Standalone background worker that measures loudness for every track that doesn't have a value
/// yet, independent of <c>MusicScanner</c>. Since the query is always "tracks lacking a value,"
/// the very first run naturally covers the whole existing library (a backfill, with no separate
/// one-off script needed); every later run (periodic or manually triggered) just catches up
/// whatever's new.
/// </summary>
public partial class LoudnessAnalysisQueueService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LoudnessAnalyzer _loudnessAnalyzer;
    private readonly PlaatOptions _options;
    private readonly ILogger<LoudnessAnalysisQueueService> _logger;
    private readonly Channel<bool> _analysisRequests = Channel.CreateBounded<bool>(1);
    private int _busy;
    private int _processedCount;
    private int _totalCount;

    // ReSharper disable once NotAccessedField.Local
    // Task.Run runs in background
    private readonly Task _processingLoop;

    public LoudnessAnalysisQueueService(
        IServiceScopeFactory scopeFactory,
        LoudnessAnalyzer loudnessAnalyzer,
        PlaatOptions options,
        ILogger<LoudnessAnalysisQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _loudnessAnalyzer = loudnessAnalyzer;
        _options = options;
        _logger = logger;
        _processingLoop = Task.Run(() => ProcessRequestsAsync(CancellationToken.None));
    }

    /// <summary>
    /// Whether an analysis run is currently in progress.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            return Volatile.Read(ref _busy) != 0;
        }
    }

    /// <summary>
    /// How many of <see cref="TotalCount"/> tracks the current (or last) run has processed.
    /// </summary>
    public int ProcessedCount
    {
        get
        {
            return Volatile.Read(ref _processedCount);
        }
    }

    /// <summary>
    /// How many tracks needed analysis when the current (or last) run started.
    /// </summary>
    public int TotalCount
    {
        get
        {
            return Volatile.Read(ref _totalCount);
        }
    }

    /// <summary>
    /// Requests an analysis run on the background processing loop if none is currently running.
    /// Returns whether a new run was started.
    /// </summary>
    public bool TryStartAnalysis()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        _analysisRequests.Writer.TryWrite(true);
        return true;
    }

    private async Task ProcessRequestsAsync(CancellationToken ct)
    {
        await foreach (var _ in _analysisRequests.Reader.ReadAllAsync(ct))
        {
            try
            {
                await RunAnalysis(ct);
            }
            catch (Exception ex)
            {
                LogRunFailed(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
    }

    private async Task RunAnalysis(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope(); // fresh scope per run, mirrors MonitoredFolderScanService
        var trackRepository = scope.ServiceProvider.GetRequiredService<ITrackRepository>();

        var trackIds = await trackRepository.GetTrackIdsNeedingLoudnessAnalysis(ct);

        Interlocked.Exchange(ref _totalCount, trackIds.Count);
        Interlocked.Exchange(ref _processedCount, 0);

        if (trackIds.Count == 0)
        {
            return;
        }

        // ffmpeg decode is CPU-heavy; default to one track at a time so a library-wide analysis
        // run doesn't compete with everything else on the box. Configurable via
        // PlaatOptions.LoudnessAnalysisMaxDegreeOfParallelism for boxes with cycles to spare.
        await Parallel.ForEachAsync(
            trackIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.LoudnessAnalysisMaxDegreeOfParallelism),
                CancellationToken = ct
            },
            async (trackId, token) => await AnalyzeTrack(trackRepository, trackId, token));

        await trackRepository.RefreshListCache(ct);
    }

    private async Task AnalyzeTrack(ITrackRepository trackRepository, uint trackId, CancellationToken ct)
    {
        try
        {
            var path = await trackRepository.GetPath(trackId, ct);

            if (path is not null)
            {
                var lufs = await _loudnessAnalyzer.MeasureIntegratedLoudnessAsync(path.Path, ct);
                await trackRepository.SetIntegratedLoudness(trackId, lufs, ct);
            }
        }
        catch (Exception ex)
        {
            LogAnalyzeFailed(ex, trackId);
        }
        finally
        {
            Interlocked.Increment(ref _processedCount);
        }
    }

    public void Dispose()
    {
        _analysisRequests.Writer.TryComplete();
    }

    [LoggerMessage(LogLevel.Error, "Loudness analysis run failed")]
    private partial void LogRunFailed(Exception ex);

    [LoggerMessage(LogLevel.Warning, "Failed to analyze loudness for track {TrackId}")]
    private partial void LogAnalyzeFailed(Exception ex, uint trackId);
}
