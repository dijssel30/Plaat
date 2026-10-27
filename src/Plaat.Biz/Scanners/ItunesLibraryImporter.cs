using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Db;
using Plaat.Db.Models;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Scanners;

/// <summary>
/// Imports per-track data (star rating, play count, playback trim points) from an iTunes/Music
/// "Library.xml" export, matching tracks by their file location.
/// </summary>
public interface IItunesLibraryImporter
{
    /// <summary>
    /// Parses the given Library.xml and applies its data to matching tracks. When
    /// <paramref name="importMissingTracks"/> is true, tracks referenced by the XML that aren't yet
    /// in the database are added first (scanning the file from disk); otherwise they are skipped and
    /// logged as information.
    /// </summary>
    Task Import(string xmlPath, int userId, bool importMissingTracks, CancellationToken ct);
}

internal partial class ItunesLibraryImporter(
    IMusicScanner musicScanner,
    IDbContextFactory<PlaatContext> contextFactory,
    FilePathReplacer pathReplacer,
    ITrackRatingRepository ratingRepository,
    ILogger<ItunesLibraryImporter> logger) : IItunesLibraryImporter
{
    private sealed record ItunesTrackEntry(
        int? Rating,
        bool RatingComputed,
        uint? PlayCount,
        int? StartTimeMs,
        int? StopTimeMs);

    public async Task Import(string xmlPath, int userId, bool importMissingTracks, CancellationToken ct)
    {
        var entriesByPath = await ParseLibrary(xmlPath, ct);

        LogParsedTracks(entriesByPath.Count, xmlPath);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var locationsByPath = await LoadLocationsByPath(context, entriesByPath.Keys, ct);

        var missingPaths = entriesByPath.Keys
            .Where(p => !locationsByPath.ContainsKey(p))
            .ToList();

        if (missingPaths.Count > 0)
        {
            if (importMissingTracks)
            {
                LogImportingMissingTracks(missingPaths.Count);

                await musicScanner.LoadFiles(missingPaths, ct);

                var newlyFound = await LoadLocationsByPath(context, missingPaths, ct);

                foreach (var (path, location) in newlyFound)
                {
                    locationsByPath[path] = location;
                }

                foreach (var path in missingPaths.Where(p => !locationsByPath.ContainsKey(p)))
                {
                    LogTrackNotImportable(path);
                }
            }
            else
            {
                foreach (var path in missingPaths)
                {
                    LogTrackSkippedNotInLibrary(path);
                }
            }
        }

        var trackIds = locationsByPath.Values.Select(l => l.TrackId).Distinct().ToList();

        var tracks = await context.Tracks
            .Include(t => t.Plays)
            .Where(t => trackIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var pendingRatings = new List<(uint TrackId, int Stars)>();

        foreach (var (path, location) in locationsByPath)
        {
            if (!entriesByPath.TryGetValue(path, out var entry) || !tracks.TryGetValue(location.TrackId, out var track))
            {
                continue;
            }

            if (entry is { Rating: { } rating, RatingComputed: false })
            {
                pendingRatings.Add((track.Id, Math.Clamp(rating / 20, TrackRating.MinStars, TrackRating.MaxStars)));
            }

            if (entry.PlayCount is { } playCount)
            {
                var oldPlayCount = track.PlayCount;

                if (ImportedPlayCount.Apply(track, playCount))
                {
                    LogPlayCountOverwritten(track.Id, path, oldPlayCount, track.PlayCount);
                }
            }

            ApplyTrimPoints(track, path, entry);
        }

        await context.SaveChangesAsync(ct);

        foreach (var (trackId, stars) in pendingRatings)
        {
            await ratingRepository.SetRating(trackId, userId, stars, ct);
        }

        LogImportFinished(tracks.Count, pendingRatings.Count);
    }

    private void ApplyTrimPoints(Track track, string path, ItunesTrackEntry entry)
    {
        var trimStart = entry.StartTimeMs is > 0 ? TimeSpan.FromMilliseconds(entry.StartTimeMs.Value) : (TimeSpan?)null;
        var trimEnd = entry.StopTimeMs is > 0 ? TimeSpan.FromMilliseconds(entry.StopTimeMs.Value) : (TimeSpan?)null;

        if (trimStart is null && trimEnd is null)
        {
            return;
        }

        // Same rules as SetTrackTrim in the API, checked against the resulting pair (an imported
        // bound is combined with the one already on the track).
        var newStart = trimStart ?? track.TrimStart;
        var newEnd = trimEnd ?? track.TrimEnd;

        var startInvalid = trimStart is not null && trimStart >= track.Duration;
        var endInvalid = trimEnd is not null && trimEnd > track.Duration;
        var orderInvalid = newStart >= newEnd;

        if (startInvalid || endInvalid || orderInvalid)
        {
            LogInvalidTrimPoints(track.Id, path, trimStart, trimEnd);

            return;
        }

        if (trimStart is not null)
        {
            track.TrimStart = trimStart;
        }

        if (trimEnd is not null)
        {
            track.TrimEnd = trimEnd;
        }
    }

    private static async Task<Dictionary<string, TrackLocation>> LoadLocationsByPath(
        PlaatContext context,
        IEnumerable<string> paths,
        CancellationToken ct)
    {
        var result = new Dictionary<string, TrackLocation>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in paths.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
        {
            var found = await context.TrackLocations
                .Where(l => chunk.Contains(l.Path))
                .ToListAsync(ct);

            foreach (var location in found)
            {
                result[location.Path] = location;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, ItunesTrackEntry>> ParseLibrary(string xmlPath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(xmlPath);

        // A real Library.xml carries a DOCTYPE referencing an external Apple DTD; ignore it rather
        // than the reader's default of either throwing on it or fetching it over the network.
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            Async = true
        });

        var doc = await XDocument.LoadAsync(xmlReader, LoadOptions.None, ct);

        var root = doc.Root?.Element("dict");
        var tracksDict = root is null ? null : GetDictField(ParseDict(root), "Tracks");

        var result = new Dictionary<string, ItunesTrackEntry>(StringComparer.OrdinalIgnoreCase);

        if (tracksDict is null)
        {
            LogNoTracksDictionary(xmlPath);
            return result;
        }

        foreach (var (_, trackElement) in ParseDict(tracksDict))
        {
            var fields = ParseDict(trackElement);

            var location = DecodeItunesLocation(GetStringField(fields, "Location"));
            var targetPath = pathReplacer.GetTargetPath(location);

            if (targetPath is null)
            {
                continue;
            }

            var playCount = GetIntField(fields, "Play Count");

            result[targetPath] = new ItunesTrackEntry(
                GetIntField(fields, "Rating"),
                GetBoolField(fields, "Rating Computed"),
                playCount is >= 0 ? (uint)playCount.Value : null,
                GetIntField(fields, "Start Time"),
                GetIntField(fields, "Stop Time"));
        }

        return result;
    }

    /// <summary>
    /// A plist &lt;dict&gt; stores its entries as a flat sequence of sibling &lt;key&gt;/value element
    /// pairs (not named elements), so pair them up manually.
    /// </summary>
    private static Dictionary<string, XElement> ParseDict(XElement dictElement)
    {
        var result = new Dictionary<string, XElement>();
        var children = dictElement.Elements().ToList();

        for (var i = 0; i + 1 < children.Count; i += 2)
        {
            if (children[i].Name.LocalName == "key")
            {
                result[children[i].Value] = children[i + 1];
            }
        }

        return result;
    }

    private static XElement? GetDictField(Dictionary<string, XElement> fields, string key)
    {
        return fields.TryGetValue(key, out var el) && el.Name.LocalName == "dict" ? el : null;
    }

    private static string? GetStringField(Dictionary<string, XElement> fields, string key)
    {
        return fields.TryGetValue(key, out var el) && el.Name.LocalName == "string" ? el.Value : null;
    }

    private static int? GetIntField(Dictionary<string, XElement> fields, string key)
    {
        return fields.TryGetValue(key, out var el) && el.Name.LocalName == "integer" && int.TryParse(el.Value, out var v)
            ? v
            : null;
    }

    private static bool GetBoolField(Dictionary<string, XElement> fields, string key)
    {
        return fields.TryGetValue(key, out var el) && el.Name.LocalName == "true";
    }

    /// <summary>
    /// Decodes a plist "Location" value - a percent-encoded <c>file://</c> URL, e.g.
    /// <c>file://localhost/Users/name/Music/track.mp3</c> or, for a library exported from Windows,
    /// <c>file://localhost/C:/Users/name/Music/track.mp3</c> - into a plain filesystem path.
    /// </summary>
    private static string? DecodeItunesLocation(string? rawLocation)
    {
        if (string.IsNullOrEmpty(rawLocation))
        {
            return null;
        }

        if (!Uri.TryCreate(rawLocation, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeFile)
        {
            return null;
        }

        var decoded = Uri.UnescapeDataString(uri.AbsolutePath);

        if (decoded is ['/', _, ':', ..])
        {
            decoded = decoded[1..];
        }

        return decoded;
    }

    [LoggerMessage(LogLevel.Information, "Parsed {Count} tracks with a file location from {Path}")]
    private partial void LogParsedTracks(int count, string path);

    [LoggerMessage(LogLevel.Information, "Importing {Count} tracks from disk that were not yet in the library")]
    private partial void LogImportingMissingTracks(int count);

    [LoggerMessage(LogLevel.Warning, "iTunes track at {Path} could not be imported (unsupported or unreadable file)")]
    private partial void LogTrackNotImportable(string path);

    [LoggerMessage(LogLevel.Information, "iTunes track at {Path} is not in the library yet; skipping (importMissingTracks is disabled)")]
    private partial void LogTrackSkippedNotInLibrary(string path);

    [LoggerMessage(LogLevel.Warning, "Updating play count for track {TrackId} ({Path}) from {OldPlayCount} to {NewPlayCount} from iTunes import")]
    private partial void LogPlayCountOverwritten(uint trackId, string path, uint oldPlayCount, uint newPlayCount);

    [LoggerMessage(LogLevel.Information, "iTunes import finished: {TrackCount} tracks matched, {RatingCount} ratings imported")]
    private partial void LogImportFinished(int trackCount, int ratingCount);

    [LoggerMessage(LogLevel.Warning, "Ignoring iTunes trim points for track {TrackId} ({Path}): start {Start} / stop {Stop} is out of range for the track duration or conflicts with its existing trim")]
    private partial void LogInvalidTrimPoints(uint trackId, string path, TimeSpan? start, TimeSpan? stop);

    [LoggerMessage(LogLevel.Warning, "No Tracks dictionary found in {Path}; nothing to import")]
    private partial void LogNoTracksDictionary(string path);
}
