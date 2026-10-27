using Microsoft.Extensions.Hosting;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class MonitoredFolderScanBackgroundService(MonitoredFolderScanService scanService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            scanService.TryStartScan();

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
