using System.Data;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Workers;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class MonitoredFoldersEndpointMapper
{
    public static void MapMonitoredFoldersEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/monitored-folders")
            .WithTags("MonitoredFolders")
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapGet(string.Empty, GetFolders)
            .WithSummary("List monitored folders")
            .Produces<IReadOnlyCollection<MonitoredFolderDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost(string.Empty, AddFolder)
            .WithSummary("Add a folder to monitor")
            .Produces<MonitoredFolderDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        apiGroup.MapDelete("{id:int}", RemoveFolder)
            .WithSummary("Stop monitoring a folder")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiGroup.MapPost("scan", TriggerScan)
            .WithSummary("Force an immediate scan of all monitored folders")
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IReadOnlyCollection<MonitoredFolderDto>> GetFolders(
        [FromServices] IMonitoredFolderRepository repository,
        CancellationToken ct)
    {
        return await repository.GetAll(ct);
    }

    private static async Task<IResult> AddFolder(
        [FromBody] AddMonitoredFolderRequest request,
        [FromServices] IMonitoredFolderRepository repository,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Results.BadRequest("Path is required.");
        }

        try
        {
            var folder = await repository.AddFolder(request.Path, ct);
            return Results.Ok(folder);
        }
        catch (DuplicateNameException)
        {
            return Results.Conflict();
        }
    }

    private static async Task RemoveFolder(
        [FromRoute] uint id,
        [FromServices] IMonitoredFolderRepository repository,
        CancellationToken ct)
    {
        await repository.RemoveFolder(id, ct);
    }

    private static IResult TriggerScan(
        [FromServices] MonitoredFolderScanService scanService)
    {
        return Results.Ok(scanService.TryStartScan());
    }
}
