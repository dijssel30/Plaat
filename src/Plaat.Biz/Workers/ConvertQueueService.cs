using System.Collections.Concurrent;
using System.Threading.Channels;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Options;
using Plaat.Biz.Qaac;
using Plaat.Db;
using Plaat.Db.Models;
using Channel = System.Threading.Channels.Channel;

namespace Plaat.Biz.Workers;

public partial class ConvertQueueService : IDisposable
{
    /// <summary>
    /// Maximum number of failed conversion attempts before an item is skipped for good, so one
    /// corrupt file cannot keep the convert loop spinning forever.
    /// </summary>
    public const int MaxAttempts = 3;

    private readonly IDbContextFactory<PlaatContext> _contextFactory;
    private readonly ILogger<ConvertQueueService> _logger;
    private readonly PlaatOptions _options;
    private readonly QaacHelper _qaacHelper;
    private readonly ConcurrentDictionary<uint, string> _currentItems = new();
    private readonly Channel<bool> _convertRequests = Channel.CreateBounded<bool>(1);
    // ReSharper disable once NotAccessedField.Local
    // Task.Run will keep running from constructor
    private readonly Task _processingLoop;

    /// <summary>
    /// Semaphore for total amount of concurrent convert jobs to be run.
    /// </summary>
    private static readonly SemaphoreSlim TaskSemaphore =
        new(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);

    /// <summary>
    /// Semaphore for reading/writing to database (only one operation allowed per db context)
    /// To make sure task in queue stays consistent across threads.
    /// </summary>
    private static readonly SemaphoreSlim DbContextSemaphore = new(1, 1);
    private int _convertBusy;

    public ConvertQueueService(
        IDbContextFactory<PlaatContext> contextFactory,
        ILogger<ConvertQueueService> logger,
        PlaatOptions options,
        QaacHelper qaacHelper)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _options = options;
        _qaacHelper = qaacHelper;
        _processingLoop = Task.Run(() => RunAsync(CancellationToken.None));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await ResetStaleWorkingItems(ct);
        }
        catch (Exception ex)
        {
            // Must not stop the processing loop from starting: TryStartConvert would keep
            // returning true while nothing ever consumes the request and _convertBusy stays 1.
            LogStaleItemsResetFailed(ex);
        }

        await ProcessConvertRequestsAsync(ct);
    }

    /// <summary>
    /// Items left marked as Working from a previous run that crashed or was killed mid-conversion
    /// would otherwise stay stuck forever (DequeueItem never picks them up again), so reset them
    /// back to not-working on startup.
    /// </summary>
    private async Task ResetStaleWorkingItems(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var staleItems = await context.ConvertQueueItems
            .Where(i => i.Working)
            .ToArrayAsync(cancellationToken);

        if (staleItems.Length == 0)
        {
            return;
        }

        foreach (var item in staleItems)
        {
            item.Working = false;
        }

        await context.SaveChangesAsync(cancellationToken);

        LogStaleItemsReset(staleItems.Length);
    }

    /// <summary>
    /// Whether a convert queue run is currently in progress on the background processing loop.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            return Volatile.Read(ref _convertBusy) != 0;
        }
    }

    /// <summary>
    /// The titles of the tracks currently being converted (possibly several, since conversions
    /// run concurrently up to a fixed degree of parallelism).
    /// </summary>
    public IReadOnlyCollection<string> CurrentItems
    {
        get
        {
            return [.. _currentItems.Values];
        }
    }

    /// <summary>
    /// Requests a run that converts every item currently in the convert queue on the background
    /// processing loop, if none is currently running. Returns whether a new run was started.
    /// </summary>
    public bool TryStartConvert()
    {
        if (Interlocked.CompareExchange(ref _convertBusy, 1, 0) != 0)
        {
            return false;
        }

        if (!_convertRequests.Writer.TryWrite(true))
        {
            Volatile.Write(ref _convertBusy, 0);
            return false;
        }

        return true;
    }

    private async Task ProcessConvertRequestsAsync(CancellationToken ct)
    {
        await foreach (var _ in _convertRequests.Reader.ReadAllAsync(ct))
        {
            try
            {
                while (await ConvertItems(ct))
                {
                }
            }
            catch (Exception ex)
            {
                LogProcessingFailed(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _convertBusy, 0);
            }
        }
    }

    /// <summary>
    /// Tries to convert an item from the queue. Returns true if a queue item was found, returns false if no items were
    /// found in the queue.
    /// </summary>
    public async Task<bool> ConvertItem(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await ConvertItem(context, cancellationToken);
    }

    private async Task<bool> ConvertItem(PlaatContext context, CancellationToken cancellationToken)
    {
        await TaskSemaphore.WaitAsync(cancellationToken);

        try
        {
            return await ConvertItemInternal(context, cancellationToken);
        }
        finally
        {
            TaskSemaphore.Release();
        }
    }

    /// <summary>
    /// Converts every item currently in the queue concurrently. Returns whether any items were found.
    /// </summary>
    public async Task<bool> ConvertItems(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var itemsInQueue = await context.ConvertQueueItems.CountAsync(i => !i.Working && i.Attempts < MaxAttempts, cancellationToken);

        var tasks = Enumerable.Range(0, itemsInQueue)
            .Select(_ => ConvertItem(context, cancellationToken));

        await Task.WhenAll(tasks);

        return itemsInQueue > 0;
    }

    private static async Task<ConvertQueueItem?> DequeueItem(
        PlaatContext context,
        CancellationToken cancellationToken)
    {
        await DbContextSemaphore.WaitAsync(cancellationToken);

        try
        {
            var item = await context.ConvertQueueItems
                .Include(i => i.TrackLocation)
                .ThenInclude(l => l.Track)
                .FirstOrDefaultAsync(i => !i.Working && i.Attempts < MaxAttempts, cancellationToken);

            if (item is null)
            {
                return null;
            }

            item.Working = true;

            await context.SaveChangesAsync(cancellationToken);

            return item;
        }
        finally
        {
            DbContextSemaphore.Release();
        }
    }

    private static async Task RequeueItem(
        ConvertQueueItem item,
        PlaatContext context,
        CancellationToken cancellationToken)
    {
        await DbContextSemaphore.WaitAsync(cancellationToken);

        try
        {
            item.Working = false;
            item.Attempts++;

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            DbContextSemaphore.Release();
        }
    }

    private static async Task RemoveItemFromQueueAndAddTrackLocation(
        ConvertQueueItem item,
        string outputPath,
        PlaatContext context,
        CancellationToken cancellationToken)
    {
        await DbContextSemaphore.WaitAsync(cancellationToken);

        try
        {
            context.Add(new TrackLocation
            {
                TrackId = item.TrackLocation.TrackId,
                Path = outputPath,
                IsSupportedFormat = true
            });

            context.Remove(item);

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            DbContextSemaphore.Release();
        }
    }

    private async Task<bool> ConvertItemInternal(PlaatContext context, CancellationToken cancellationToken)
    {
        var item = await DequeueItem(context, cancellationToken);

        if (item is null)
        {
            LogNoItemFound();
            return false;
        }

        LogConverting(item.TrackLocation.Track.Title);

        _currentItems[item.Id] = item.TrackLocation.Track.Title;

        try
        {
            var fileHash = item.TrackLocation.Track.FileHash;
            var path = item.TrackLocation.Path;

            var outputPath = Path.Join(_options.TargetReconvertedPath, fileHash) + ".m4a";

            if (!File.Exists(outputPath))
            {
                // Convert to a temp file first so a crash mid-write never leaves a truncated file at outputPath.
                // The ".tmp" goes before the extension because FFmpeg/qaac infer the container from it.
                var tempPath = Path.Join(_options.TargetReconvertedPath, fileHash) + ".tmp.m4a";
                // A hard kill mid-conversion leaves the temp file behind, and FFmpeg refuses to overwrite it.
                TryDeleteFile(tempPath);
                try
                {
                    await ConvertItem(path, tempPath);
                    File.Move(tempPath, outputPath, true);
                }
                catch (Exception ex)
                {
                    TryDeleteFile(tempPath);
                    LogConvertFailed(ex, item.TrackLocation.Track.Title);
                    await RequeueItem(item, context, cancellationToken);
                    return true;
                }
            }

            if (!File.Exists(outputPath))
            {
                LogOutputMissing(outputPath);
                await RequeueItem(item, context, cancellationToken);
                return true;
            }

            LogConverted(item.TrackLocation.Track.Title);
            await RemoveItemFromQueueAndAddTrackLocation(item, outputPath, context, cancellationToken);

            return true;
        }
        finally
        {
            _currentItems.TryRemove(item.Id, out _);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task ConvertItem(string path, string outputPath)
    {
        if (_options.UseQaac)
        {
            await _qaacHelper.ConvertFileUsingQaac(path, outputPath);
        }
        else
        {
            await FFMpegArguments
                .FromFileInput(path)
                .OutputToFile(outputPath, false, o => o
                    .WithAudioCodec(AudioCodec.Aac)
                    .WithAudioBitrate(AudioQuality.VeryHigh)
                    .WithCustomArgument("-map_metadata 0"))
                .ProcessAsynchronously();
        }
    }

    public void Dispose()
    {
        _convertRequests.Writer.TryComplete();
    }

    [LoggerMessage(LogLevel.Warning, "Reset {Count} convert queue item(s) left in a stuck working state from a previous run")]
    private partial void LogStaleItemsReset(int count);

    [LoggerMessage(LogLevel.Error, "Failed to reset stale convert queue items on startup")]
    private partial void LogStaleItemsResetFailed(Exception ex);

    [LoggerMessage(LogLevel.Error, "Convert queue processing failed")]
    private partial void LogProcessingFailed(Exception ex);

    [LoggerMessage(LogLevel.Debug, "Did not find an item")]
    private partial void LogNoItemFound();

    [LoggerMessage(LogLevel.Debug, "Working on converting item {Title}")]
    private partial void LogConverting(string title);

    [LoggerMessage(LogLevel.Error, "Could not convert item {Title}. Requeuing")]
    private partial void LogConvertFailed(Exception ex, string title);

    [LoggerMessage(LogLevel.Error, "Expected output file to exist, but it appears not to {outputPath}. Requeuing")]
    private partial void LogOutputMissing(string outputPath);

    [LoggerMessage(LogLevel.Debug, "Converted file {Title}. removing from queue")]
    private partial void LogConverted(string title);
}
