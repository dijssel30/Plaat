using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Plaat.Biz.Options;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class CoverEndpointMapper
{
    private static byte[] DefaultCoverArt { get; }
        = File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Plaat.jpeg"));

    // Stable weak tag for the fallback image so browsers can 304-revalidate it too.
    private static readonly EntityTagHeaderValue DefaultCoverEtag = new("\"default\"", isWeak: true);

    // Covers are content-addressed (their ETag is the file hash) and rarely change, so allow a
    // short local cache window before the browser even revalidates. The album page still cache-busts
    // its own <img> after an upload, so a stale grid thumbnail self-corrects within the hour.
    private static readonly TimeSpan CoverMaxAge = TimeSpan.FromHours(1);

    public static void MapCoverEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/cover")
            .WithTags("Cover")
            .RequireAuthorization();

        apiGroup.MapGet("track/{id:int}", GetCoverArtByTrackId)
            .WithSummary("Get cover art by track ID")
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Jpeg)
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Png)
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Webp)
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("album/{id:int}", GetCoverArtByAlbumId)
            .WithSummary("Get cover art by album ID")
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Jpeg)
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Png)
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Webp)
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> GetCoverArtByTrackId(
        [FromRoute] uint id,
        [FromServices] ITrackRepository repository,
        [FromServices] PlaatOptions options,
        HttpContext ctx,
        CancellationToken ct)
    {
        var coverArt = await repository.GetCoverArt(id, ct);

        return BuildCoverResult(coverArt, options, ctx);
    }

    private static async Task<IResult> GetCoverArtByAlbumId(
        [FromRoute] uint id,
        [FromServices] IAlbumRepository repository,
        [FromServices] PlaatOptions options,
        HttpContext ctx,
        CancellationToken ct)
    {
        var coverArt = await repository.GetCoverArt(id, ct);

        return BuildCoverResult(coverArt, options, ctx);
    }

    private static IResult BuildCoverResult(CoverArtDto? coverArt, PlaatOptions options, HttpContext ctx)
    {
        ctx.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            MaxAge = CoverMaxAge
        };

        if (coverArt is null
            || !coverArt.MimeType.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase))
        {
            return DefaultImageResult();
        }

        var path = GetRealPath(options, coverArt);

        if (path is null || new FileInfo(path).Length == 0)
        {
            return DefaultImageResult();
        }

        // A content hash is the natural ETag: browsers revalidate with If-None-Match and get a
        // bodyless 304 instead of re-downloading the image on every grid scroll / revisit.
        return Results.File(
            Path.GetFullPath(path),
            contentType: coverArt.MimeType,
            entityTag: new EntityTagHeaderValue($"\"{coverArt.Hash}\""),
            enableRangeProcessing: false);
    }

    private static IResult DefaultImageResult()
    {
        return Results.File(DefaultCoverArt, MediaTypeNames.Image.Jpeg, entityTag: DefaultCoverEtag);
    }

    private static string? GetRealPath(
        PlaatOptions options,
        CoverArtDto coverArt)
    {
        if (File.Exists(coverArt.Path))
        {
            return coverArt.Path;
        }

        var fullPath = Path.Join(options.ImagesStoragePath, coverArt.Path);

        return File.Exists(fullPath)
            ? fullPath
            : null;
    }
}
