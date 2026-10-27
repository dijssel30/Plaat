using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Workers;
using Plaat.Contract.Models;

namespace Plaat.Api.EndpointMappers;

public static class TasksEndpointMapper
{
    public static void MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/tasks")
            .WithTags("Tasks")
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapGet("status", GetStatus)
            .WithSummary("Get whether a folder scan or a convert queue run is currently in progress")
            .Produces<TasksStatusDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static TasksStatusDto GetStatus(
        [FromServices] MonitoredFolderScanService scanService,
        [FromServices] ConvertQueueService convertQueueService,
        [FromServices] LoudnessAnalysisQueueService loudnessAnalysisQueueService)
    {
        return new TasksStatusDto(
            scanService.IsBusy,
            scanService.CurrentFolder,
            convertQueueService.IsBusy,
            convertQueueService.CurrentItems,
            loudnessAnalysisQueueService.IsBusy,
            loudnessAnalysisQueueService.ProcessedCount,
            loudnessAnalysisQueueService.TotalCount);
    }
}
