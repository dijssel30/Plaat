using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class StatsEndpointMapper
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/stats")
            .WithTags("Stats")
            .RequireAuthorization();

        apiGroup.MapGet(string.Empty, GetStats)
            .WithSummary("Get listening stats for a time range and listener scope, plus library totals")
            .Produces<StatsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<StatsDto> GetStats(
        [FromServices] IStatsRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct,
        [FromQuery] StatsRange? range = null,
        [FromQuery] StatsScope? scope = null)
    {
        var userId = scope == StatsScope.Me ? user.GetUserId() : (int?)null;

        return await repository.GetStats(range ?? StatsRange.All, userId, ct);
    }
}
