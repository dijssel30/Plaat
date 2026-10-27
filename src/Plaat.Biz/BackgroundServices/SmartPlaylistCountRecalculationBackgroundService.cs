using Microsoft.Extensions.Hosting;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public sealed class SmartPlaylistCountRecalculationBackgroundService(SmartPlaylistCountService countService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await countService.RecalculateAll(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
