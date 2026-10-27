using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;
using Plaat.Db;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "plaat-backup-" + Guid.NewGuid().ToString("N"));

    public DatabaseBackupServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private DatabaseBackupService Build(int keep, TimeSpan? interval = null, IDbContextFactory<PlaatContext>? factoryOverride = null)
    {
        var options = new DbContextOptionsBuilder<PlaatContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "source.db")};Pooling=False").Options;
        using (var ctx = new PlaatContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        var factory = Substitute.For<IDbContextFactory<PlaatContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PlaatContext(options)));

        var plaat = new PlaatOptions
        {
            ImagesStoragePath = "x",
            TargetReconvertedPath = "x",
            JwtOptions = new JwtOptions { Key = "k" },
            BackupSettings = new BackupSettings
            {
                Enabled = true,
                TargetFolder = Path.Combine(_dir, "backups"),
                KeepCount = keep,
                Interval = interval ?? TimeSpan.FromDays(1)
            }
        };
        return new DatabaseBackupService(factoryOverride ?? factory, plaat, NullLogger<DatabaseBackupService>.Instance);
    }

    private static DatabaseBackupService BuildWithSettings(BackupSettings? settings)
    {
        var plaat = new PlaatOptions
        {
            ImagesStoragePath = "x",
            TargetReconvertedPath = "x",
            JwtOptions = new JwtOptions { Key = "k" },
            BackupSettings = settings
        };

        return new DatabaseBackupService(
            Substitute.For<IDbContextFactory<PlaatContext>>(), plaat, NullLogger<DatabaseBackupService>.Instance);
    }

    [Fact]
    public async Task CreateBackup_WritesFileAndPrunesOldest()
    {
        var service = Build(keep: 2);
        var folder = service.Settings.TargetFolder!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plaat-20200101-000000.db"), "old");
        File.WriteAllText(Path.Combine(folder, "plaat-20200102-000000.db"), "old");

        var path = await service.CreateBackup();

        File.Exists(path).ShouldBeTrue();
        new FileInfo(path).Length.ShouldBeGreaterThan(0);
        Directory.GetFiles(folder, "plaat-*.db").Length.ShouldBe(2);
        File.Exists(Path.Combine(folder, "plaat-20200101-000000.db")).ShouldBeFalse();
    }

    [Fact]
    public void GetNextDue_NoBackups_UsesStartTime()
    {
        var service = Build(keep: 2);
        var due = service.GetNextDue(new DateTime(2026, 1, 1, 10, 0, 0));
        due.ShouldBe(new DateTime(2026, 1, 2, 3, 0, 0));
    }

    [Fact]
    public void GetNextDue_WithBackup_AddsInterval()
    {
        var service = Build(keep: 2, interval: TimeSpan.FromHours(6));
        Directory.CreateDirectory(service.Settings.TargetFolder!);
        File.WriteAllText(Path.Combine(service.Settings.TargetFolder!, "plaat-20260101-030000.db"), "x");
        service.GetNextDue(DateTime.Now).ShouldBe(new DateTime(2026, 1, 1, 9, 0, 0));
    }

    [Fact]
    public void Settings_WhenNotConfigured_FallsBackToDisabledDefaults()
    {
        var service = BuildWithSettings(null);

        service.Settings.Enabled.ShouldBeFalse();
        service.Settings.TargetFolder.ShouldBeNull();
    }

    [Fact]
    public void GetNextDue_NoBackupsBeforeStartTime_IsTodayAtStartTime()
    {
        var service = BuildWithSettings(new BackupSettings { StartTime = new TimeOnly(3, 0) });

        service.GetNextDue(new DateTime(2026, 1, 1, 1, 0, 0)).ShouldBe(new DateTime(2026, 1, 1, 3, 0, 0));
    }

    [Fact]
    public void GetNextDue_NonPositiveInterval_DefaultsToOneDay()
    {
        var service = Build(keep: 2, interval: TimeSpan.Zero);
        Directory.CreateDirectory(service.Settings.TargetFolder!);
        File.WriteAllText(Path.Combine(service.Settings.TargetFolder!, "plaat-20260101-030000.db"), "x");

        service.GetNextDue(DateTime.Now).ShouldBe(new DateTime(2026, 1, 2, 3, 0, 0));
    }

    [Fact]
    public void GetNextDue_IgnoresFilesWhoseNameIsNotABackupTimestamp()
    {
        var service = Build(keep: 2, interval: TimeSpan.FromHours(1));
        Directory.CreateDirectory(service.Settings.TargetFolder!);
        File.WriteAllText(Path.Combine(service.Settings.TargetFolder!, "plaat-not-a-date.db"), "x");

        service.GetNextDue(new DateTime(2026, 1, 1, 10, 0, 0)).ShouldBe(new DateTime(2026, 1, 2, 3, 0, 0));
    }

    [Fact]
    public async Task CreateBackup_WithoutTargetFolder_Throws()
    {
        var service = BuildWithSettings(new BackupSettings { Enabled = true, TargetFolder = " " });

        await Should.ThrowAsync<InvalidOperationException>(() => service.CreateBackup());
    }

    [Fact]
    public async Task CreateBackup_WhenDatabaseUnavailable_LeavesNoTempFileAndRethrows()
    {
        var failing = Substitute.For<IDbContextFactory<PlaatContext>>();
        failing.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<PlaatContext>>(_ => throw new InvalidOperationException("db down"));
        var service = Build(keep: 2, factoryOverride: failing);

        await Should.ThrowAsync<InvalidOperationException>(() => service.CreateBackup());

        Directory.GetFiles(service.Settings.TargetFolder!).ShouldBeEmpty();
    }
}
