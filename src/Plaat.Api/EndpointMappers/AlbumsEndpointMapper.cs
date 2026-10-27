using System.Collections.ObjectModel;
using System.Net.Mime;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Options;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class AlbumsEndpointMapper
{
    public static void MapAlbumsEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/albums")
            .WithTags("Albums")
            .RequireAuthorization();

        apiGroup.MapGet(string.Empty, GetAllAlbums)
            .WithSummary("Get list of all albums")
            .Produces<IReadOnlyCollection<AlbumOnlyDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}", GetAlbum)
            .WithSummary("Get album details by its ID")
            .Produces<AlbumDetailsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}/favorite", GetAlbumFavorite)
            .WithSummary("Get whether the current user has favorited an album")
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/favorite", AddAlbumFavorite)
            .WithSummary("Mark an album as a favorite for the current user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapDelete("{id:int}/favorite", RemoveAlbumFavorite)
            .WithSummary("Remove an album from the current user's favorites")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("{id:int}/cover", UploadAlbumCover)
            .WithSummary("Upload and persist a new cover image for an album")
            .DisableAntiforgery()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.AdminPolicy);
    }

    private const long MaxCoverArtBytes = 15 * 1024 * 1024;

    private static readonly ReadOnlyDictionary<string, string> AllowedCoverArtTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [MediaTypeNames.Image.Jpeg] = ".jpg",
        [MediaTypeNames.Image.Png] = ".png",
        [MediaTypeNames.Image.Webp] = ".webp"
    }.AsReadOnly();

    private static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return MediaTypeNames.Image.Jpeg;
        }

        if (bytes.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return MediaTypeNames.Image.Png;
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return MediaTypeNames.Image.Webp;
        }

        return null;
    }

    private static async Task<IResult> UploadAlbumCover(
        [FromRoute] uint id,
        IFormFile file,
        [FromServices] IAlbumRepository repository,
        [FromServices] PlaatOptions options,
        CancellationToken ct)
    {
        switch (file.Length)
        {
            case 0:
                return Results.BadRequest("The uploaded file is empty.");
            case > MaxCoverArtBytes:
                return Results.BadRequest("The image is too large (15 MB maximum).");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var contentType = DetectImageContentType(bytes);

        if (contentType is null || !AllowedCoverArtTypes.TryGetValue(contentType, out var extension))
        {
            return Results.BadRequest("Unsupported image type. Use JPEG, PNG or WebP.");
        }

        var hash = Convert.ToHexString(MD5.HashData(bytes));
        var fileName = $"{hash}{extension}";

        var fullFileName = Path.Join(options.ImagesStoragePath, fileName);

        if (!File.Exists(fullFileName))
        {
            Directory.CreateDirectory(options.ImagesStoragePath);

            await File.WriteAllBytesAsync(fullFileName, bytes, ct);
        }

        var updated = await repository.SetCoverArt(id, fileName, hash, contentType, ct);

        return updated
            ? Results.Ok()
            : Results.NotFound();
    }

    private static async Task<IReadOnlyCollection<AlbumOnlyDto>> GetAllAlbums(
        [FromServices] IAlbumRepository repository,
        CancellationToken ct)
    {
        return await repository.GetAlbums(ct);
    }

    private static async Task<IResult> GetAlbum(
        [FromRoute] uint id,
        [FromServices] IAlbumRepository repository,
        CancellationToken ct)
    {
        var album = await repository.GetAlbum(id, ct);

        return album is null
            ? Results.NoContent()
            : Results.Ok(album);
    }

    private static async Task<bool> GetAlbumFavorite(
        [FromRoute] uint id,
        [FromServices] IAlbumRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await repository.IsAlbumFavorite(id, user.GetUserId(), ct);
    }

    private static async Task AddAlbumFavorite(
        [FromRoute] uint id,
        [FromServices] IAlbumRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.SetAlbumFavorite(id, user.GetUserId(), true, ct);
    }

    private static async Task RemoveAlbumFavorite(
        [FromRoute] uint id,
        [FromServices] IAlbumRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.SetAlbumFavorite(id, user.GetUserId(), false, ct);
    }
}
