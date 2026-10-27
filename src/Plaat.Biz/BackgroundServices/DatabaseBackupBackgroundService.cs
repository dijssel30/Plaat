using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Workers;

namespace Plaat.Biz.BackgroundServices;

public partial class DatabaseBackupBackgroundService(
    DatabaseBackupService backupService,
    ILogger<DatabaseBackupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!backupService.Settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(backupService.Settings.TargetFolder))
        {
            LogBackupTargetFolderMissing();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var wait = backupService.GetNextDue(DateTime.Now) - DateTime.Now;
                if (wait > TimeSpan.Zero)
                {
                    await DelayInChunks(wait, stoppingToken);
                }

                await backupService.CreateBackup(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogBackupFailed(ex);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    // Task.Delay caps at ~49 days, so long intervals are waited out in pieces.
    private static async Task DelayInChunks(TimeSpan wait, CancellationToken ct)
    {
        var max = TimeSpan.FromDays(7);
        while (wait > TimeSpan.Zero)
        {
            var step = wait > max ? max : wait;
            await Task.Delay(step, ct);
            wait -= step;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Database backup is enabled but BackupSettings.TargetFolder is not set; backups are disabled")]
    private partial void LogBackupTargetFolderMissing();

    [LoggerMessage(LogLevel.Error, "Database backup failed; retrying in an hour")]
    private partial void LogBackupFailed(Exception ex);
}
