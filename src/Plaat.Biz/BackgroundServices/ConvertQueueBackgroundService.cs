using Microsoft.Extensions.Hosting;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class ConvertQueueBackgroundService(ConvertQueueService queueService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            queueService.TryStartConvert();

            await Task.Delay(TimeSpan.FromMinutes(50), stoppingToken);
        }
    }
}
