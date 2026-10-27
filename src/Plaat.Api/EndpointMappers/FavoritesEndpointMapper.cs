using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class FavoritesEndpointMapper
{
    public static void MapFavoritesEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/favorites")
            .WithTags("Favorites")
            .RequireAuthorization();

        apiGroup.MapGet("albums", GetFavoriteAlbums)
            .WithSummary("Get every album the current user has marked as a favorite")
            .Produces<IReadOnlyCollection<AlbumOnlyDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("artists", GetFavoriteArtists)
            .WithSummary("Get every artist the current user has marked as a favorite")
            .Produces<IReadOnlyCollection<ArtistOnlyDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IReadOnlyCollection<AlbumOnlyDto>> GetFavoriteAlbums(
        [FromServices] IAlbumRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await repository.GetFavoriteAlbums(user.GetUserId(), ct);
    }

    private static async Task<IReadOnlyCollection<ArtistOnlyDto>> GetFavoriteArtists(
        [FromServices] IArtistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await repository.GetFavoriteArtists(user.GetUserId(), ct);
    }
}
