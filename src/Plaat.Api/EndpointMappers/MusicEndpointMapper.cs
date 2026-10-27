using System.Collections.ObjectModel;
using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Scanners;
using Plaat.Biz.Workers;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static partial class MusicEndpointMapper
{
    public static void MapMusicEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/music")
            .WithTags("Music")
            .RequireAuthorization();

        apiGroup.MapPost("folder/{folderName:nonfile}/scan", ScanMusicInFolder)
            .WithSummary("Queue a scan of the given folder, adding all new files to the database")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost("file/{csvFileName:file}/scan", ScanMusicFromFile)
            .WithSummary("Queue a scan of the given csv file, adding all new files to the database")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost("file/{xmlFileName:file}/import-itunes-library", ImportItunesLibrary)
            .WithSummary(
                "Queue an import of star ratings, play counts and playback trim points from an iTunes/Music Library.xml file")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapGet(string.Empty, GetMusicPage)
            .WithSummary("Get one filtered/sorted page of tracks, plus the total match count")
            .Produces<PagedResult<TrackDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("ids", GetMusicIds)
            .WithSummary("Get the ordered IDs of every track matching a query (for building a play queue)")
            .Produces<IReadOnlyList<uint>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("by-ids", GetMusicByIds)
            .WithSummary("Resolve a set of track IDs to their summaries, preserving order")
            .Produces<IReadOnlyList<TrackDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("recently-added", GetRecentlyAdded)
            .WithSummary("Get tracks that were added in the last week")
            .Produces<RecentlyAddedDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("recently-played", GetRecentlyPlayed)
            .WithSummary("Get the most recently played distinct tracks")
            .Produces<IReadOnlyList<TrackDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}/details", GetTrackDetails)
            .WithSummary("Get track details, including file stats, locations and play history")
            .Produces<TrackDetailsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/locations/{locationId:int}/default", SetDefaultLocation)
            .WithSummary("Mark a track location as the default location for a track")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/trim", SetTrackTrim)
            .WithSummary("Set (or clear) a track's admin-only start/end playback trim points")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost("{id:int}/reload-metadata", ReloadTrackMetadata)
            .WithSummary("Force a track's metadata to be re-read from its file on disk")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapGet("possibly-corrupt", GetPossiblyCorrupt)
            .WithSummary("Get all tracks flagged as possibly corrupt during scanning")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces<IReadOnlyCollection<PossiblyCorruptTrackDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapGet("{id:int}", PlayMusic)
            .WithSummary("Play a music track")
            .Produces(StatusCodes.Status206PartialContent, contentType: MediaTypeNames.Application.Octet)
            .Produces(StatusCodes.Status206PartialContent, contentType: CustomMediaTypeNames.Audio.Mp3)
            .Produces(StatusCodes.Status206PartialContent, contentType: CustomMediaTypeNames.Audio.Flac)
            .Produces(StatusCodes.Status206PartialContent, contentType: CustomMediaTypeNames.Audio.Mp4)
            .Produces(StatusCodes.Status206PartialContent, contentType: CustomMediaTypeNames.Audio.Aac)
            .Produces(StatusCodes.Status206PartialContent, contentType: CustomMediaTypeNames.Audio.Ogg)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static IResult ScanMusicInFolder(
        [FromRoute] string folderName,
        [FromServices] MonitoredFolderScanService scanService)
    {
        folderName = folderName.Replace("%2F", "/");

        return Results.Ok(scanService.TryStartFolderScan(folderName));
    }

    private static IResult ScanMusicFromFile(
        [FromRoute] string csvFileName,
        [FromServices] MonitoredFolderScanService scanService)
    {
        csvFileName = csvFileName.Replace("%2F", "/");

        if (!File.Exists(csvFileName) || !Path.GetExtension(csvFileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        return Results.Ok(scanService.TryStartCsvScan(csvFileName));
    }

    private static IResult ImportItunesLibrary(
        [FromRoute] string xmlFileName,
        [FromServices] MonitoredFolderScanService scanService,
        ClaimsPrincipal user,
        [FromQuery] bool importMissingTracks = false)
    {
        xmlFileName = xmlFileName.Replace("%2F", "/");

        if (!File.Exists(xmlFileName) || !Path.GetExtension(xmlFileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        return Results.Ok(scanService.TryStartItunesXmlImport(xmlFileName, user.GetUserId(), importMissingTracks));
    }

    private const int MaxPageSize = 500;

    private static async Task<PagedResult<TrackDto>> GetMusicPage(
        [FromServices] ITrackRepository repository,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] TrackSortField sort = TrackSortField.Title,
        [FromQuery] bool desc = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var query = new TrackQuery(
            search,
            sort,
            desc,
            Math.Max(skip, 0),
            Math.Clamp(take, 1, MaxPageSize));

        return await repository.GetTracksPage(query, ct);
    }

    private static async Task<IReadOnlyList<uint>> GetMusicIds(
        [FromServices] ITrackRepository repository,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] TrackSortField sort = TrackSortField.Title,
        [FromQuery] bool desc = false)
    {
        return await repository.GetTrackIds(new TrackQuery(search, sort, desc, 0, 0), ct);
    }

    private static async Task<IReadOnlyList<TrackDto>> GetMusicByIds(
        [FromBody] uint[] ids,
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        return await repository.GetByIds(ids, ct);
    }

    private static async Task<RecentlyAddedDto> GetRecentlyAdded(
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        return await repository.GetRecentlyAdded(DateTime.UtcNow.AddDays(-7), ct);
    }

    private static async Task<IReadOnlyList<TrackDto>> GetRecentlyPlayed(
        [FromServices] ITrackRepository repository,
        CancellationToken ct,
        [FromQuery] int take = 50)
    {
        return await repository.GetRecentlyPlayed(Math.Clamp(take, 1, 200), ct);
    }

    private static async Task<IResult> GetTrackDetails(
        [FromRoute] uint id,
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        var track = await repository.GetTrackDetails(id, ct);

        return track is null
            ? Results.NoContent()
            : Results.Ok(track);
    }

    private static async Task<IResult> SetDefaultLocation(
        [FromRoute] uint id,
        [FromRoute] uint locationId,
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        var success = await repository.SetDefaultLocation(id, locationId, ct);

        return success
            ? Results.Ok()
            : Results.NotFound();
    }

    private static async Task<IResult> SetTrackTrim(
        [FromRoute] uint id,
        [FromBody] SetTrackTrimRequest request,
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        var duration = await repository.GetDuration(id, ct);

        if (duration is null)
        {
            return Results.NotFound();
        }

        if (request.TrimStart is { } start && (start < TimeSpan.Zero || start >= duration.Value))
        {
            return Results.BadRequest();
        }

        if (request.TrimEnd is { } end && (end <= TimeSpan.Zero || end > duration.Value))
        {
            return Results.BadRequest();
        }

        if (request.TrimStart is not null && request.TrimEnd is not null && request.TrimStart >= request.TrimEnd)
        {
            return Results.BadRequest();
        }

        await repository.SetTrimPoints(id, request.TrimStart, request.TrimEnd, ct);

        return Results.Ok();
    }

    private static async Task<IResult> ReloadTrackMetadata(
        [FromRoute] uint id,
        [FromServices] IMusicScanner scanner,
        CancellationToken ct)
    {
        var success = await scanner.ReloadTrackMetadata(id, ct);

        return success
            ? Results.Ok()
            : Results.NotFound();
    }

    private static async Task<IReadOnlyCollection<PossiblyCorruptTrackDto>> GetPossiblyCorrupt(
        [FromServices] ITrackRepository repository,
        CancellationToken ct)
    {
        return await repository.GetPossiblyCorrupt(ct);
    }

    private static readonly ReadOnlyDictionary<string, string> AudioContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = CustomMediaTypeNames.Audio.Mp3,
        [".flac"] = CustomMediaTypeNames.Audio.Flac,
        [".wav"] = CustomMediaTypeNames.Audio.Wav,
        [".m4a"] = CustomMediaTypeNames.Audio.Mp4,
        [".aac"] = CustomMediaTypeNames.Audio.Aac,
        [".ogg"] = CustomMediaTypeNames.Audio.Ogg,
        [".opus"] = CustomMediaTypeNames.Audio.Opus,
        [".aiff"] = CustomMediaTypeNames.Audio.Aiff,
        [".aif"] = CustomMediaTypeNames.Audio.Aiff,
    }.AsReadOnly();

    /// <summary>
    /// Picks the Content-Type from the file actually being served, since the stored per-track
    /// value comes from TagLib (e.g. "taglib/mp3") and does not follow a converted file.
    /// </summary>
    private static string GetAudioContentType(string filePath, string storedMimeType)
    {
        if (AudioContentTypes.TryGetValue(Path.GetExtension(filePath), out var contentType))
        {
            return contentType;
        }

        return storedMimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            ? storedMimeType
            : MediaTypeNames.Application.Octet;
    }

    private static async Task<IResult> PlayMusic(
        [FromRoute] uint id,
        [FromServices] ITrackRepository repository,
        [FromServices] ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var trackPath = await repository.GetPath(id, ct);

        if (trackPath is null)
        {
            LogTrackLocationMissing(logger, id);

            return Results.NotFound();
        }

        var filePath = trackPath.Path;
        var mimeType = GetAudioContentType(filePath, trackPath.MimeType);

        if (!File.Exists(filePath))
        {
            LogTrackFileMissing(logger, id, filePath);

            return Results.NotFound();
        }

        var fileInfo = new FileInfo(filePath);
        var totalLength = fileInfo.Length;

        context.Response.Headers.AcceptRanges = "bytes";
        var rangeHeader = context.Request.Headers.Range.ToString();

        if (string.IsNullOrEmpty(rangeHeader))
        {
            return Results.File(
                filePath,
                contentType: mimeType,
                enableRangeProcessing: true);
        }

        if (!TryParseRange(rangeHeader, totalLength, out var start, out var end))
        {
            context.Response.Headers.ContentRange = $"bytes */{totalLength}";

            return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        var length = end - start + 1;

        context.Response.StatusCode = StatusCodes.Status206PartialContent;
        context.Response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";
        context.Response.ContentLength = length;
        context.Response.ContentType = mimeType;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        stream.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024];
        var remaining = length;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);

            if (read is 0)
            {
                break;
            }

            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }

        return Results.Empty;
    }

    private static bool TryParseRange(string rangeHeader, long totalLength, out long start, out long end)
    {
        start = 0;
        end = 0;

        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rangeSpec = rangeHeader["bytes=".Length..];

        // Multi-range requests (e.g. "bytes=0-10,20-30") are not supported.
        if (rangeSpec.Contains(','))
        {
            return false;
        }

        var parts = rangeSpec.Split('-');

        if (parts.Length != 2)
        {
            return false;
        }

        var startText = parts[0].Trim();
        var endText = parts[1].Trim();

        if (startText.Length == 0)
        {
            // Suffix range, e.g. "bytes=-500" meaning "the last 500 bytes".
            if (!long.TryParse(endText, out var suffixLength) || suffixLength <= 0)
            {
                return false;
            }

            start = Math.Max(totalLength - suffixLength, 0);
            end = totalLength - 1;

            return true;
        }

        if (!long.TryParse(startText, out start) || start < 0)
        {
            return false;
        }

        if (endText.Length == 0)
        {
            end = totalLength - 1;
        }
        else if (!long.TryParse(endText, out end))
        {
            return false;
        }

        if (start > end || start >= totalLength)
        {
            return false;
        }

        end = Math.Min(end, totalLength - 1);

        return true;
    }

    [LoggerMessage(LogLevel.Warning, "No track location found for {id}")]
    private static partial void LogTrackLocationMissing(ILogger logger, uint id);

    [LoggerMessage(LogLevel.Error, "Expected file with id {id} to exist at location {filePath}, but it did not")]
    private static partial void LogTrackFileMissing(ILogger logger, uint id, string filePath);
}
