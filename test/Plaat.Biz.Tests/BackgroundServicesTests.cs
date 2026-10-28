using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.BackgroundServices;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Biz.Qaac;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Biz.Workers;
using Plaat.Db;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

/// <summary>
/// The hosted-service loops: each one triggers its worker on start, waits out its interval, and
/// stops cleanly when the host shuts down.
/// </summary>
public sealed class BackgroundServicesTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly BizTestFixture _fixture = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("plaat-bg-").FullName;

    public void Dispose()
    {
        _fixture.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Runs the protected ExecuteAsync directly, so a test controls its stopping token.
    /// </summary>
    private static Task Execute(BackgroundService service, CancellationToken stoppingToken)
    {
        var method = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (Task)method.Invoke(service, [stoppingToken])!;
    }

    private static PlaatOptions Options(BackupSettings? backup = null)
    {
        return new PlaatOptions
        {
            ImagesStoragePath = "x",
            TargetReconvertedPath = "x",
            JwtOptions = new JwtOptions { Key = "k" },
            BackupSettings = backup
        };
    }

    private static IServiceScopeFactory ScopeFactoryFor<T>(T service)
        where T : class
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(T)).Returns(service);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return scopeFactory;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    [Fact]
    public async Task SmartPlaylistCountRecalculation_RecalculatesUntilStopped()
    {
        var repository = Substitute.For<IPlaylistRepository>();
        var service = new SmartPlaylistCountRecalculationBackgroundService(
            new SmartPlaylistCountService(repository, NullLogger<SmartPlaylistCountService>.Instance),
            Options());
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => repository.ReceivedCalls().Any());
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task LoudnessAnalysis_StartsARunUntilStopped()
    {
        var repository = Substitute.For<ITrackRepository>();
        repository.GetTrackIdsNeedingLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns([]);
        using var queue = new LoudnessAnalysisQueueService(
            ScopeFactoryFor(repository),
            new LoudnessAnalyzer(NullLogger<LoudnessAnalyzer>.Instance),
            Options(),
            NullLogger<LoudnessAnalysisQueueService>.Instance);
        var service = new LoudnessAnalysisBackgroundService(queue, Options());
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => repository.ReceivedCalls().Any());
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task MonitoredFolderScan_StartsAScanUntilStopped()
    {
        _fixture.SeedMonitoredFolder(Path.Combine(_dir, "missing-folder"));
        var scanner = Substitute.For<IMusicScanner>();
        var scanService = new MonitoredFolderScanService(
            _fixture.DbFactory, ScopeFactoryFor(scanner), NullLogger<MonitoredFolderScanService>.Instance);
        var service = new MonitoredFolderScanBackgroundService(scanService, Options());
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => !scanService.IsBusy);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task ConvertQueue_StartsAConvertRunUntilStopped()
    {
        using var queue = new ConvertQueueService(
            _fixture.DbFactory,
            NullLogger<ConvertQueueService>.Instance,
            Options(),
            new QaacHelper(NullLogger<QaacHelper>.Instance, Options()));
        var service = new ConvertQueueBackgroundService(queue, Options());
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => !queue.IsBusy);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Loops_AlreadyStopped_DoNothing()
    {
        var repository = Substitute.For<IPlaylistRepository>();
        var trackRepository = Substitute.For<ITrackRepository>();
        using var loudness = new LoudnessAnalysisQueueService(
            ScopeFactoryFor(trackRepository),
            new LoudnessAnalyzer(NullLogger<LoudnessAnalyzer>.Instance),
            Options(),
            NullLogger<LoudnessAnalysisQueueService>.Instance);
        using var convert = new ConvertQueueService(
            _fixture.DbFactory,
            NullLogger<ConvertQueueService>.Instance,
            Options(),
            new QaacHelper(NullLogger<QaacHelper>.Instance, Options()));
        var scanService = new MonitoredFolderScanService(
            _fixture.DbFactory, Substitute.For<IServiceScopeFactory>(), NullLogger<MonitoredFolderScanService>.Instance);

        await Execute(new SmartPlaylistCountRecalculationBackgroundService(
            new SmartPlaylistCountService(repository, NullLogger<SmartPlaylistCountService>.Instance), Options()), new CancellationToken(true));
        await Execute(new LoudnessAnalysisBackgroundService(loudness, Options()), new CancellationToken(true));
        await Execute(new ConvertQueueBackgroundService(convert, Options()), new CancellationToken(true));
        await Execute(new MonitoredFolderScanBackgroundService(scanService, Options()), new CancellationToken(true));

        repository.ReceivedCalls().ShouldBeEmpty();
        loudness.IsBusy.ShouldBeFalse();
        convert.IsBusy.ShouldBeFalse();
        scanService.IsBusy.ShouldBeFalse();
    }

    // ---- database backup ----

    private DatabaseBackupBackgroundService BackupService(BackupSettings? settings, IDbContextFactory<PlaatContext>? factory = null)
    {
        var backup = new DatabaseBackupService(
            factory ?? _fixture.DbFactory, Options(settings), NullLogger<DatabaseBackupService>.Instance);

        return new DatabaseBackupBackgroundService(backup, Options(), NullLogger<DatabaseBackupBackgroundService>.Instance);
    }

    private string BackupFolder
    {
        get
        {
            return Path.Combine(_dir, "backups");
        }
    }

    [Fact]
    public async Task DatabaseBackup_NotConfigured_ReturnsImmediately()
    {
        await Execute(BackupService(null), CancellationToken.None).WaitAsync(Timeout);

        Directory.Exists(BackupFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task DatabaseBackup_EnabledWithoutTargetFolder_ReturnsImmediately()
    {
        await Execute(BackupService(new BackupSettings { Enabled = true }), CancellationToken.None).WaitAsync(Timeout);
    }

    [Fact]
    public async Task DatabaseBackup_Overdue_BacksUpNowThenWaitsForNextAndStopsCleanly()
    {
        Directory.CreateDirectory(BackupFolder);
        await File.WriteAllTextAsync(Path.Combine(BackupFolder, "plaat-20000101-000000.db"), "old");
        var service = BackupService(new BackupSettings { Enabled = true, TargetFolder = BackupFolder, KeepCount = 5 });
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => Directory.GetFiles(BackupFolder, "plaat-*.db").Length == 2);
        await cts.CancelAsync();

        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task DatabaseBackup_NextDueFarAway_WaitsInChunksAndStopsCleanly()
    {
        Directory.CreateDirectory(BackupFolder);
        var recent = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(Path.Combine(BackupFolder, $"plaat-{recent}.db"), "recent");
        var service = BackupService(new BackupSettings
        {
            Enabled = true,
            TargetFolder = BackupFolder,
            Interval = TimeSpan.FromDays(60)
        });
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();

        await run.WaitAsync(Timeout);
        Directory.GetFiles(BackupFolder).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DatabaseBackup_WhenBackupFails_LogsAndWaitsBeforeRetrying()
    {
        var failing = Substitute.For<IDbContextFactory<PlaatContext>>();
        var attempted = false;
        failing.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns<Task<PlaatContext>>(_ =>
        {
            attempted = true;
            throw new InvalidOperationException("db down");
        });
        Directory.CreateDirectory(BackupFolder);
        await File.WriteAllTextAsync(Path.Combine(BackupFolder, "plaat-20000101-000000.db"), "old");
        var service = BackupService(new BackupSettings { Enabled = true, TargetFolder = BackupFolder }, failing);
        using var cts = new CancellationTokenSource();

        var run = Execute(service, cts.Token);
        await WaitUntil(() => attempted);
        await Task.Delay(50);
        run.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
    }
}
