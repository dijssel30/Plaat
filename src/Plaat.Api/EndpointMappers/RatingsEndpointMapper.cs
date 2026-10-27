using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class RatingsEndpointMapper
{
    public static void MapRatingsEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/ratings")
            .WithTags("Ratings")
            .RequireAuthorization();

        apiGroup.MapGet("tracks/{id:int}", GetTrackRating)
            .WithSummary("Get the current user's star rating for a track (0 when unrated)")
            .Produces<TrackRatingDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("tracks/{id:int}", SetTrackRating)
            .WithSummary("Set the current user's star rating for a track (0 clears it)")
            .Produces<TrackRatingDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<TrackRatingDto> GetTrackRating(
        [FromRoute] uint id,
        [FromServices] ITrackRatingRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return new TrackRatingDto
        {
            TrackId = id,
            Stars = await repository.GetRating(id, user.GetUserId(), ct)
        };
    }

    private static async Task<IResult> SetTrackRating(
        [FromRoute] uint id,
        [FromBody] SetTrackRatingRequest request,
        [FromServices] ITrackRatingRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (request.Stars is < TrackRatingDto.MinStars or > TrackRatingDto.MaxStars)
        {
            return Results.BadRequest(
                $"Stars must be between {TrackRatingDto.MinStars} and {TrackRatingDto.MaxStars}.");
        }

        var updated = await repository.SetRating(id, user.GetUserId(), request.Stars, ct);

        return updated
            ? Results.Ok(new TrackRatingDto { TrackId = id, Stars = request.Stars })
            : Results.NotFound();
    }
}
