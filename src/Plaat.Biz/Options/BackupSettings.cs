namespace Plaat.Biz.Options;

public sealed class BackupSettings
{
    /// <summary>
    /// Master on/off switch for the automatic database backup.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Folder the backups are written to. Created if it does not exist. Required when
    /// <see cref="Enabled"/> is true.
    /// </summary>
    public string? TargetFolder { get; init; }

    /// <summary>
    /// How often a backup is made. Defaults to once a day.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Local time of day of the first backup, used when the target folder holds no backup yet.
    /// Later backups follow <see cref="Interval"/> after the previous one. Defaults to 03:00 (night).
    /// </summary>
    public TimeOnly StartTime { get; init; } = new(3, 0);

    /// <summary>
    /// How many backups to keep. The oldest ones are deleted after each backup.
    /// </summary>
    public int KeepCount { get; init; } = 7;
}
