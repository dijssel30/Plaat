using Microsoft.Extensions.Hosting;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class SmartPlaylistCountRecalculationBackgroundService(
    SmartPlaylistCountService countService,
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
            await countService.RecalculateAll(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
