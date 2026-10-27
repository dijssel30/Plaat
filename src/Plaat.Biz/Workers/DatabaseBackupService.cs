using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Options;
using Plaat.Db;

namespace Plaat.Biz.Workers;

/// <summary>
/// Makes consistent snapshots of the SQLite database using SQLite's online backup API (safe while
/// the API is reading/writing) and prunes old ones.
/// </summary>
public partial class DatabaseBackupService(
    IDbContextFactory<PlaatContext> contextFactory,
    PlaatOptions plaatOptions,
    ILogger<DatabaseBackupService> logger)
{
    private const string FilePrefix = "plaat-";
    private const string FileExtension = ".db";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    public BackupSettings Settings
    {
        get
        {
            return plaatOptions.BackupSettings ?? new BackupSettings();
        }
    }

    /// <summary>
    /// Local time the next backup is due, based on the newest backup in the target folder.
    /// </summary>
    public DateTime GetNextDue(DateTime now)
    {
        var interval = Settings.Interval > TimeSpan.Zero ? Settings.Interval : TimeSpan.FromDays(1);
        var latest = GetBackupFiles().Select(f => f.Timestamp).Cast<DateTime?>().FirstOrDefault();
        if (latest is not null)
        {
            return latest.Value + interval;
        }

        var start = now.Date + Settings.StartTime.ToTimeSpan();
        return start > now ? start : start.AddDays(1);
    }

    public async Task<string> CreateBackup(CancellationToken ct = default)
    {
        var folder = Settings.TargetFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("BackupSettings.TargetFolder is not set.");
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, FilePrefix + DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture) + FileExtension);
        var temp = target + ".tmp";
        File.Delete(temp);

        try
        {
            await using (var context = await contextFactory.CreateDbContextAsync(ct))
            {
                var source = (SqliteConnection)context.Database.GetDbConnection();
                await source.OpenAsync(ct);

                // BackupDatabase is synchronous; keep it off the request/background thread.
                await Task.Run(() =>
                {
                    using var destination = new SqliteConnection($"Data Source={temp};Pooling=False");
                    destination.Open();
                    source.BackupDatabase(destination);
                }, ct);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Best effort; the original exception is the one worth surfacing.
            }

            throw;
        }


        LogBackupWritten(target);

        Prune();
        return target;
    }

    private void Prune()
    {
        var keep = Math.Max(1, Settings.KeepCount);
        foreach (var file in GetBackupFiles().Skip(keep))
        {
            try
            {
                File.Delete(file.Path);
                LogOldBackupDeleted(file.Path);
            }
            catch (Exception ex)
            {
                LogOldBackupDeleteFailed(ex, file.Path);
            }
        }
    }

    /// <summary>
    /// Backups in the target folder, newest first.
    /// </summary>
    private List<(string Path, DateTime Timestamp)> GetBackupFiles()
    {
        var folder = Settings.TargetFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var result = new List<(string, DateTime)>();
        foreach (var path in Directory.EnumerateFiles(folder, FilePrefix + "*" + FileExtension))
        {
            var name = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (DateTime.TryParseExact(name, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            {
                result.Add((path, ts));
            }
        }

        return [.. result.OrderByDescending(f => f.Item2)];
    }

    [LoggerMessage(LogLevel.Information, "Database backup written to {Path}")]
    private partial void LogBackupWritten(string path);

    [LoggerMessage(LogLevel.Information, "Deleted old database backup {Path}")]
    private partial void LogOldBackupDeleted(string path);

    [LoggerMessage(LogLevel.Warning, "Could not delete old database backup {Path}")]
    private partial void LogOldBackupDeleteFailed(Exception ex, string path);
}
