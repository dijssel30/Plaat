using Microsoft.Extensions.Hosting;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class ConvertQueueBackgroundService(
    ConvertQueueService queueService,
    PlaatOptions plaatOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!plaatOptions.EnableBackgroundServices)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            queueService.TryStartConvert();

            await Task.Delay(TimeSpan.FromMinutes(50), stoppingToken);
        }
    }
}
