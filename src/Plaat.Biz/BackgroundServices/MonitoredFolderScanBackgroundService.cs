using Microsoft.Extensions.Hosting;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class MonitoredFolderScanBackgroundService(
    MonitoredFolderScanService scanService,
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
            scanService.TryStartScan();

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
