using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class ArtistsEndpointMapper
{
    public static void MapArtistsEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/artists")
            .WithTags("Artists")
            .RequireAuthorization();

        apiGroup.MapGet(string.Empty, GetAllArtists)
            .WithSummary("Get list of all artists")
            .Produces<IReadOnlyCollection<ArtistOnlyDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}", GetArtist)
            .WithSummary("Get artist details by its ID")
            .Produces<ArtistDetailsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("{id:int}/enrich", EnrichArtist)
            .WithSummary(
                "Trigger Discogs enrichment for an artist if not yet attempted and wait for it to " +
                "finish, returning the refreshed artist details")
            .Produces<ArtistDetailsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}/favorite", GetArtistFavorite)
            .WithSummary("Get whether the current user has favorited an artist")
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/favorite", AddArtistFavorite)
            .WithSummary("Mark an artist as a favorite for the current user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapDelete("{id:int}/favorite", RemoveArtistFavorite)
            .WithSummary("Remove an artist from the current user's favorites")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("{id:int}/discogs-image", GetDiscogsImage)
            .WithSummary("Get the Discogs-sourced image for an artist, if one has been loaded")
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Image.Jpeg)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("{id:int}/discogs/refresh", RefreshDiscogsInfo)
            .WithSummary("Force a fresh Discogs name-based lookup for an artist")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost("{id:int}/discogs/force", ForceDiscogsArtist)
            .WithSummary("Force enrichment from a specific Discogs artist ID, bypassing the exact-name-match check")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IReadOnlyCollection<ArtistOnlyDto>> GetAllArtists(
        [FromServices] IArtistRepository repository,
        CancellationToken ct)
    {
        return await repository.GetArtists(ct);
    }

    private static async Task<IResult> GetArtist(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        CancellationToken ct)
    {
        var artist = await repository.GetArtist(id, ct);

        return artist is null
            ? Results.NoContent()
            : Results.Ok(artist);
    }

    private static async Task<IResult> EnrichArtist(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        [FromServices] ArtistEnrichmentQueueService enrichmentQueueService,
        CancellationToken ct)
    {
        var artist = await repository.GetArtist(id, ct);

        if (artist is null)
        {
            return Results.NoContent();
        }

        if (artist.DiscogsLookupStatus is ArtistDiscogsLookupStatus.NotAttempted
            && await enrichmentQueueService.EnrichAndWait(id, ct))
        {
            artist = await repository.GetArtist(id, ct);
        }

        return Results.Ok(artist);
    }

    private static async Task<bool> GetArtistFavorite(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await repository.IsArtistFavorite(id, user.GetUserId(), ct);
    }

    private static async Task AddArtistFavorite(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.SetArtistFavorite(id, user.GetUserId(), true, ct);
    }

    private static async Task RemoveArtistFavorite(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.SetArtistFavorite(id, user.GetUserId(), false, ct);
    }

    private static async Task<IResult> GetDiscogsImage(
        [FromRoute] uint id,
        [FromServices] IArtistRepository repository,
        [FromServices] PlaatOptions options,
        CancellationToken ct)
    {
        var imagePath = await repository.GetDiscogsImagePath(id, ct);

        if (imagePath is null)
        {
            return Results.NoContent();
        }

        var fullPath = Path.Join(options.ImagesStoragePath, imagePath);

        if (!File.Exists(fullPath))
        {
            return Results.NoContent();
        }

        var mimeType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => MediaTypeNames.Image.Png,
            ".webp" => MediaTypeNames.Image.Webp,
            _ => MediaTypeNames.Image.Jpeg
        };

        var fileStream = File.OpenRead(fullPath);

        return Results.File(fileStream, mimeType);
    }

    private static async Task<IResult> RefreshDiscogsInfo(
        [FromRoute] uint id,
        [FromServices] ArtistEnrichmentQueueService enrichmentQueueService,
        CancellationToken ct)
    {
        await enrichmentQueueService.EnqueueForRefresh(id, ct);

        return Results.Ok();
    }

    private static async Task<IResult> ForceDiscogsArtist(
        [FromRoute] uint id,
        [FromBody] ForceDiscogsArtistRequest request,
        [FromServices] ArtistEnrichmentQueueService enrichmentQueueService,
        CancellationToken ct)
    {
        await enrichmentQueueService.EnqueueForced(id, request.DiscogsArtistId, ct);

        return Results.Ok();
    }
}
