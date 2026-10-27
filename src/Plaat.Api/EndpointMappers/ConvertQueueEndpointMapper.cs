using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Workers;

namespace Plaat.Api.EndpointMappers;

public static class ConvertQueueEndpointMapper
{
    public static void MapConvertQueueEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/queue")
            .WithTags("Queue")
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapPost("convert", TriggerConvert)
            .WithSummary("Queue a run that converts every item currently in the convert queue")
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static void TriggerConvert(
        [FromServices] ConvertQueueService convertQueueService)
    {
        convertQueueService.TryStartConvert();
    }
}
