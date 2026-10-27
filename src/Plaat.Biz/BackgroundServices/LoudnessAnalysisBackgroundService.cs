using Microsoft.Extensions.Hosting;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class LoudnessAnalysisBackgroundService(LoudnessAnalysisQueueService queueService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            queueService.TryStartAnalysis();

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
