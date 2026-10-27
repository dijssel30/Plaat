using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz;
using Plaat.Contract.Models;

namespace Plaat.Api.EndpointMappers;

public static class SearchEndpointMapper
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/search")
            .WithTags("Search")
            .RequireAuthorization()
            .MapGet(string.Empty, Search)
            .WithSummary("Search tracks, albums and artists; each section is capped server-side")
            .Produces<SearchResultsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<SearchResultsDto> Search(
        [FromServices] LibrarySearchService search,
        CancellationToken ct,
        [FromQuery] string? q = null,
        [FromQuery] int take = 50)
    {
        return await search.Search(q, Math.Clamp(take, 1, 200), ct);
    }
}
