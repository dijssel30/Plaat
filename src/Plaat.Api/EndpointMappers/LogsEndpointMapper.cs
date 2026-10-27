using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class LogsEndpointMapper
{
    private const int MaxLimit = 1000;

    public static void MapLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/logs")
            .WithTags("Logs")
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapGet(string.Empty, GetLogs)
            .WithSummary("List recent warning-and-above log entries, optionally filtered by level and message")
            .Produces<IReadOnlyCollection<LogEntryDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IReadOnlyCollection<LogEntryDto>> GetLogs(
        [FromServices] ILogEntryRepository repository,
        CancellationToken ct,
        [FromQuery] string[]? levels = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 200)
    {
        var query = new LogQuery(levels, search, Math.Clamp(limit, 1, MaxLimit));

        return await repository.GetRecent(query, ct);
    }
}
