using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Workers;

namespace Plaat.Api.EndpointMappers;

public static class LoudnessAnalysisEndpointMapper
{
    public static void MapLoudnessAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/loudness-analysis")
            .WithTags("LoudnessAnalysis")
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapPost("scan", TriggerScan)
            .WithSummary("Queue a run that measures loudness for every track that doesn't have a value yet")
            .Produces<bool>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static bool TriggerScan(
        [FromServices] LoudnessAnalysisQueueService loudnessAnalysisQueueService)
    {
        return loudnessAnalysisQueueService.TryStartAnalysis();
    }
}
