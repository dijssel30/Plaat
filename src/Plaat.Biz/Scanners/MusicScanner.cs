using System.Collections.Concurrent;
using System.Globalization;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Db;
using Plaat.Db.Models;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Scanners;

/// <summary>
/// Ingests music files from a folder or a CSV of paths into the database.
/// </summary>
public interface IMusicScanner
{
    /// <summary>
    /// Scan a folder and add all new music files to the database
    /// </summary>
    Task LoadFilesFromFolder(string folder, CancellationToken ct);

    /// <summary>
    /// Load all files in the csv and add all new files to the database
    /// </summary>
    Task LoadFilesFromCsv(string csvPath, CancellationToken ct);

    /// <summary>
    /// Add the given files to the database as new tracks (files already known are left untouched).
    /// Used by imports that reference tracks from an external source (e.g. an iTunes library) that
    /// aren't in the database yet.
    /// </summary>
    Task LoadFiles(IReadOnlyCollection<string> filePaths, CancellationToken ct);

    /// <summary>
    /// Re-reads tags/media info from disk for a track and updates its stored metadata, regardless of
    /// whether the file has changed. Returns false if the track, or none of its locations, could be found.
    /// </summary>
    Task<bool> ReloadTrackMetadata(uint trackId, CancellationToken ct);
}

internal partial class MusicScanner(
    IDbContextFactory<PlaatContext> contextFactory,
    ILogger<MusicScanner> logger,
    ILogger<TrackFactory> trackFactoryLogger,
    PlaatOptions options,
    MediaInfoParser parser,
    ILibraryCache cache,
    ITrackRepository trackRepository,
    IAlbumRepository albumRepository,
    IArtistRepository artistRepository,
    FilePathReplacer pathReplacer) : IMusicScanner, IDisposable
{
    private static readonly IReadOnlyCollection<string> SupportedMusicFileExtensions =
        [".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aiff"];

    private readonly TrackFactory _trackFactory = new(options, parser, trackFactoryLogger);
    private readonly Dictionary<string, TrackInfo> _trackInfoForFilePath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _precalcualtedHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Semaphore for max concurrent music scans (calculates hash, so don't want too many at the same time)
    /// </summary>
    private readonly SemaphoreSlim _concurrentScansSemaphore = new(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);

    /// <summary>
    /// Semaphore for writing to the csv hash file
    /// </summary>
    private readonly SemaphoreSlim _writeToHashFileSemaphore = new(1, 1);

    /// <summary>
    /// Called after a batch of scan changes is persisted. Rather than evicting the expensive
    /// whole-library list caches (and making the next visitor wait for a cold rebuild), it
    /// re-populates them in place. The cheap per-entity detail entries, which a scan can change
    /// wholesale, are just dropped and lazily rebuilt on next access.
    /// </summary>
    private async Task RefreshLibraryCaches(CancellationToken ct)
    {
        await Task.WhenAll(
            trackRepository.RefreshListCache(ct),
            albumRepository.RefreshListCache(ct),
            artistRepository.RefreshListCache(ct));

        cache.Remove(CacheKeys.LibraryStats);
        cache.Remove(CacheKeys.RecentlyAdded);
        cache.RemoveByPrefix(CacheKeys.RecentlyPlayedPrefix);
        cache.RemoveByPrefix(CacheKeys.TrackDetailsPrefix);
        cache.RemoveByPrefix(CacheKeys.AlbumDetailsPrefix);
        cache.RemoveByPrefix(CacheKeys.ArtistDetailsPrefix);
        cache.RemoveByPrefix(CacheKeys.TrackDurationPrefix);
        cache.RemoveByPrefix(CacheKeys.SmartPlaylistTracksPrefix);
    }

    public async Task LoadFilesFromFolder(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        if (options.PrecalculatedHashCsvPath is not null
            && File.Exists(options.PrecalculatedHashCsvPath))
        {
            await LoadPreCalculatedHashes(options.PrecalculatedHashCsvPath);
        }

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        await BuildTracksFromFiles(context, files, ct);

        _trackFactory.Reset();

        // BuildTracksFromFiles already refreshes the caches after its final save.
    }

    /// <summary>
    /// Csv need to be in format "%path%","%play_count%" without header
    /// </summary>
    public async Task LoadFilesFromCsv(
        string csvPath,
        CancellationToken ct)
    {
        if (options.PrecalculatedHashCsvPath is not null
            && File.Exists(options.PrecalculatedHashCsvPath))
        {
            await LoadPreCalculatedHashes(options.PrecalculatedHashCsvPath);
        }

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var files = new List<string>();
        while (await csv.ReadAsync())
        {
            var path = csv.GetField(0);
            path = pathReplacer.GetTargetPath(path);

            var playCount = csv.GetField<uint>(1);

            DateTime? date = null;

            if (csv.Parser.Count > 2)
            {
                var rawDate = csv.GetField(2);
                date = rawDate is null
                    ? null
                    : DateTime.ParseExact(
                        rawDate,
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture
                    );
            }

            if (path is null || !Path.Exists(path))
            {
                LogFileMissing(logger, path);
                continue;
            }

            files.Add(path);
            _trackInfoForFilePath[path] = new TrackInfo(playCount, date);
        }

        LogLoadedFromCsv(logger, files.Count);

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            await BuildTracksFromFiles(context, files, ct);

            _trackFactory.Reset();

            // BuildTracksFromFiles already refreshes the caches after its final save.
        }
        finally
        {
            // Don't let this import's counts leak into later scans.
            _trackInfoForFilePath.Clear();
        }
    }

    public async Task LoadFiles(IReadOnlyCollection<string> filePaths, CancellationToken ct)
    {
        if (options.PrecalculatedHashCsvPath is not null
            && File.Exists(options.PrecalculatedHashCsvPath))
        {
            await LoadPreCalculatedHashes(options.PrecalculatedHashCsvPath);
        }

        var files = new List<string>();

        foreach (var path in filePaths)
        {
            if (!Path.Exists(path))
            {
                LogFileMissing(logger, path);
                continue;
            }

            files.Add(path);
        }

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        await BuildTracksFromFiles(context, files, ct);

        _trackFactory.Reset();

        // BuildTracksFromFiles already refreshes the caches after its final save.
    }

    public async Task<bool> ReloadTrackMetadata(uint trackId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var track = await context.Tracks
            .Include(t => t.TrackLocations)
            .FirstOrDefaultAsync(t => t.Id == trackId, ct);

        var location = track?.TrackLocations
            .OrderByDescending(l => l.Default)
            .ThenByDescending(l => l.IsSupportedFormat)
            .FirstOrDefault(l => File.Exists(l.Path));

        if (location is null)
        {
            return false;
        }

        await _trackFactory.PreloadAsync(context, ct);

        var fileHash = await TrackFactory.ComputeHashAsync(location.Path, ct);

        await ReloadLocationMetadata(context, location, fileHash, ct);

        await context.SaveChangesAsync(ct);

        _trackFactory.Reset();

        await RefreshLibraryCaches(ct);

        return true;
    }

    /// <summary>
    /// Quotes a field for CSV output, doubling embedded quotes so it round-trips through CsvHelper.
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private async Task LoadPreCalculatedHashes(string precalculatedHashCsvPath)
    {
        using var reader = new StreamReader(precalculatedHashCsvPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        while (await csv.ReadAsync())
        {
            var path = csv.GetField(0);
            var hash = csv.GetField(1);

            if (path is not null && hash is { Length: 32 })
            {
                _precalcualtedHashes.TryAdd(path, hash);
            }
        }
    }

    private async Task BuildTracksFromFiles(
        PlaatContext context,
        List<string> files,
        CancellationToken cancellationToken)
    {
        var supportedFiles = files
            .Where(f => SupportedMusicFileExtensions.Contains(
                Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var importsPlayCounts = _trackInfoForFilePath.Count > 0;

        var trackLocationsByPath =
            await LoadAllExistingTracksInDb(context, supportedFiles, importsPlayCounts, cancellationToken);

        await _trackFactory.PreloadAsync(context, cancellationToken);

        // Pass 1: stream-hash each new/changed file and build the list of files that actually need
        // processing. Already-known files whose size hasn't changed are skipped without hashing.
        var filesToProcessBag = new ConcurrentBag<(string path, string hash)>();
        var locationsToReloadBag = new ConcurrentBag<(TrackLocation location, string hash)>();
        await Task.WhenAll(
            supportedFiles.Select(filePath =>
                CalculateHashAndAddToList(filePath, trackLocationsByPath, filesToProcessBag, locationsToReloadBag, cancellationToken)));

        var filesToProcess = filesToProcessBag.ToList();
        var locationsToReload = locationsToReloadBag.ToList();

        // Batch-load all tracks that share a hash with a file to process.
        var existingTracksWithSameHash =
            await LoadExistingTracksWithSameHash(context, filesToProcess, importsPlayCounts, cancellationToken);

        var pendingChanges = 0;

        // Re-import: raise the imported base play count of tracks whose location was already known.
        foreach (var (path, info) in _trackInfoForFilePath)
        {
            if (trackLocationsByPath.TryGetValue(path, out var knownLocation)
                && ImportedPlayCount.Apply(knownLocation.Track, info.PlayCount))
            {
                pendingChanges++;
            }
        }

        // Pass 2: re-read metadata for files whose size changed on disk, and persist in batches of 1000
        foreach (var (location, fileHash) in locationsToReload)
        {
            try
            {
                await ReloadLocationMetadata(context, location, fileHash, cancellationToken);

                LogMetadataReloaded(logger, location.Path);
            }
            catch (Exception ex)
            {
                LogReloadFailed(logger, ex, location.Path);
                continue;
            }

            pendingChanges++;

            if (pendingChanges >= 1000)
            {
                await context.SaveChangesAsync(cancellationToken);

                await RefreshLibraryCaches(cancellationToken);
                pendingChanges = 0;
            }
        }

        // Pass 3: create/update entities and persist in batches of 1000
        foreach (var (filePath, fileHash) in filesToProcess)
        {
            if (existingTracksWithSameHash.TryGetValue(fileHash, out var existingTrack))
            {
                // Track already exists (in DB or created earlier in this scan); just add new location
                if (_trackInfoForFilePath.TryGetValue(filePath, out var duplicateInfo)
                    && ImportedPlayCount.Apply(existingTrack, duplicateInfo.PlayCount))
                {
                    pendingChanges++;
                }

                if (existingTrack.TrackLocations.All(l => l.Path != filePath))
                {
                    existingTrack.TrackLocations.Add(new TrackLocation { Path = filePath });
                    pendingChanges++;
                }
            }
            else
            {
                Track newTrack;
                try
                {
                    var trackInfo = _trackInfoForFilePath.GetValueOrDefault(filePath);

                    newTrack =
                        await _trackFactory.BuildTrackFromFile(context, filePath, fileHash, trackInfo);

                    LogTrackCreated(logger, newTrack.Title, pendingChanges);
                }
                catch (Exception ex)
                {
                    LogTrackCreateFailed(logger, ex, filePath);
                    continue;
                }

                existingTracksWithSameHash[fileHash] = newTrack; // register so later duplicates in this scan are found
                pendingChanges++;
            }

            if (pendingChanges >= 1000)
            {
                await context.SaveChangesAsync(cancellationToken);

                await RefreshLibraryCaches(cancellationToken);
                pendingChanges = 0;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        await RefreshLibraryCaches(cancellationToken);
    }

    private static async Task<Dictionary<string, Track>> LoadExistingTracksWithSameHash(
        PlaatContext context,
        List<(string path, string hash)> filesToProcess,
        bool importsPlayCounts,
        CancellationToken cancellationToken)
    {
        var hashToTrack = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);

        var distinctHashes = filesToProcess
            .Select(f => f.hash)
            .Distinct()
            .ToList();

        foreach (var chunk in distinctHashes.Chunk(500))
        {
            var query = context.Tracks
                .Include(t => t.TrackLocations)
                .TagWith("existingTrackWithHash")
                .Where(t => chunk.Contains(t.FileHash));

            if (importsPlayCounts)
            {
                query = query.Include(t => t.Plays);
            }

            var found = await query.ToListAsync(cancellationToken);

            foreach (var track in found)
            {
                hashToTrack[track.FileHash] = track;
            }
        }

        return hashToTrack;
    }

    private static async Task<Dictionary<string, TrackLocation>> LoadAllExistingTracksInDb(
        PlaatContext context,
        List<string> supportedFiles,
        bool importsPlayCounts,
        CancellationToken cancellationToken)
    {
        var trackLocationsByPath = new Dictionary<string, TrackLocation>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in supportedFiles.Chunk(500))
        {
            var query = context.TrackLocations
                .Include(t => t.Track)
                .TagWith("ExistingTracks")
                .Where(x => chunk.Contains(x.Path));

            if (importsPlayCounts)
            {
                query = query.Include(t => t.Track).ThenInclude(t => t.Plays);
            }

            var locations = await query.ToListAsync(cancellationToken);

            foreach (var loc in locations)
            {
                trackLocationsByPath[loc.Path] = loc;
            }
        }

        return trackLocationsByPath;
    }

    private async Task CalculateHashAndAddToList(
        string filePath,
        Dictionary<string, TrackLocation> trackLocationsByPath,
        ConcurrentBag<(string path, string hash)> filesToProcessBag,
        ConcurrentBag<(TrackLocation location, string hash)> locationsToReloadBag,
        CancellationToken cancellationToken)
    {
        await _concurrentScansSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (trackLocationsByPath.TryGetValue(filePath, out var existing))
            {
                // Cheap size check first - avoids hashing every already-known file on every scan.
                var currentFileSize = new FileInfo(filePath).Length;

                if (existing.Track.FileSizeBytes == currentFileSize)
                {
                    return;
                }

                var changedFileHash = await TrackFactory.ComputeHashAsync(filePath, cancellationToken);
                locationsToReloadBag.Add((existing, changedFileHash));

                return;
            }

            var precalculatedHash = _precalcualtedHashes.GetValueOrDefault(filePath);

            var fileHash = precalculatedHash ?? await TrackFactory.ComputeHashAsync(filePath, cancellationToken);

            if (precalculatedHash is null
                && options.PrecalculatedHashCsvPath is not null
                && File.Exists(options.PrecalculatedHashCsvPath))
            {
                await _writeToHashFileSemaphore.WaitAsync(cancellationToken);

                try
                {
                    await File.AppendAllLinesAsync(
                        options.PrecalculatedHashCsvPath,
                        [
                            $"{EscapeCsvField(filePath)},{EscapeCsvField(fileHash)}"
                        ], cancellationToken);
                }
                finally
                {
                    _writeToHashFileSemaphore.Release();
                }
            }

            LogFileHashCalculated(logger, filePath, fileHash);

            filesToProcessBag.Add((filePath, fileHash));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A locked/unreadable/vanished file must not abort hashing for the rest of the folder.
            LogHashFailed(logger, ex, filePath);
        }
        finally
        {
            _concurrentScansSemaphore.Release();
        }
    }

    private async Task ReloadLocationMetadata(
        PlaatContext context,
        TrackLocation location,
        string fileHash,
        CancellationToken ct)
    {
        // The reload replaces these many-to-many collections wholesale. Unless the existing links are
        // loaded first, EF can't tell they're already there and re-inserts them, which fails on the
        // join table's primary key and aborts the whole save.
        var trackEntry = context.Entry(location.Track);
        await trackEntry.Collection(t => t.Artists).LoadAsync(ct);
        await trackEntry.Collection(t => t.Genres).LoadAsync(ct);

        var isSupportedFormat = await _trackFactory.ReloadTrackMetadataAsync(location.Track, location.Path, fileHash);
        location.IsSupportedFormat = isSupportedFormat;

        if (!isSupportedFormat
            && !await context.ConvertQueueItems.AnyAsync(i => i.TrackLocationId == location.Id, ct))
        {
            context.Add(new ConvertQueueItem { TrackLocation = location });
        }
    }

    public void Dispose()
    {
        _concurrentScansSemaphore.Dispose();
    }

    [LoggerMessage(LogLevel.Debug, "Loaded {fileCount} files from csv")]
    private static partial void LogLoadedFromCsv(ILogger logger, int fileCount);

    [LoggerMessage(LogLevel.Error, "File does not exist {path}")]
    private static partial void LogFileMissing(ILogger logger, string? path);

    [LoggerMessage(LogLevel.Information, "Reloaded metadata for {Path} after its file size changed on disk")]
    private static partial void LogMetadataReloaded(ILogger logger, string path);

    [LoggerMessage(LogLevel.Error, "Failed to reload metadata for {filePath}")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex, string filePath);

    [LoggerMessage(LogLevel.Error, "Failed to hash {filePath}; skipping it")]
    private static partial void LogHashFailed(ILogger logger, Exception ex, string filePath);

    [LoggerMessage(LogLevel.Debug, "Created new track {Title}. Pending changes {pendingChanges}")]
    private static partial void LogTrackCreated(ILogger logger, string title, int pendingChanges);

    [LoggerMessage(LogLevel.Error, "Failed to create new track {filePath}")]
    private static partial void LogTrackCreateFailed(ILogger logger, Exception ex, string filePath);

    [LoggerMessage(LogLevel.Debug, "Calculated file hash for {filePath}: {fileHash}")]
    private static partial void LogFileHashCalculated(ILogger logger, string filePath, string fileHash);
}
