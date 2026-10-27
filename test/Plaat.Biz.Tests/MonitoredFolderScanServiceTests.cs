using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Biz.Workers;
using Plaat.Db;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class MonitoredFolderScanServiceTests
{
    private static (MonitoredFolderScanService service, IMusicScanner scanner, IItunesLibraryImporter importer) BuildService(
        BizTestFixture fixture)
    {
        var scanner = Substitute.For<IMusicScanner>();
        var importer = Substitute.For<IItunesLibraryImporter>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IMusicScanner)).Returns(scanner);
        serviceProvider.GetService(typeof(IItunesLibraryImporter)).Returns(importer);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var logger = Substitute.For<ILogger<MonitoredFolderScanService>>();
        var service = new MonitoredFolderScanService(fixture.DbFactory, scopeFactory, logger);

        return (service, scanner, importer);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
            }
            catch
            {
                // The condition may race a concurrent write on the shared in-memory SQLite
                // connection (e.g. "database is locked"); treat that as "not ready yet" and retry.
            }

            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public void TryStartScan_FirstCall_ReturnsTrue()
    {
        using var fixture = new BizTestFixture();
        var (service, _, _) = BuildService(fixture);

        var result = service.TryStartScan();

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartScan_WhileScanInProgress_ReturnsFalse()
    {
        using var fixture = new BizTestFixture();
        fixture.SeedMonitoredFolder("/music");
        var (service, scanner, _) = BuildService(fixture);

        var blockGate = new TaskCompletionSource();
        scanner.LoadFilesFromFolder(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => blockGate.Task);

        service.TryStartScan().ShouldBeTrue();

        var scannerWasInvoked = await WaitUntil(() => scanner.ReceivedCalls().Any(), TimeSpan.FromSeconds(2));
        scannerWasInvoked.ShouldBeTrue();

        service.TryStartScan().ShouldBeFalse();

        blockGate.SetResult();
    }

    [Fact]
    public async Task TryStartScan_AfterScanCompletes_LockIsReleasedAndCanStartAgain()
    {
        using var fixture = new BizTestFixture();
        var (service, scanner, _) = BuildService(fixture);
        scanner.LoadFilesFromFolder(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        service.TryStartScan().ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartScan_ScansEachMonitoredFolderAndUpdatesLastScannedAt()
    {
        using var fixture = new BizTestFixture();
        var folder = fixture.SeedMonitoredFolder("/music/library");
        var (service, scanner, _) = BuildService(fixture);
        scanner.LoadFilesFromFolder(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        service.TryStartScan().ShouldBeTrue();

        var scanned = await WaitUntil(() =>
        {
            using var context = fixture.CreateContext();
            return context.MonitoredFolders.AsNoTracking().Single(f => f.Id == folder.Id).LastScannedAt is not null;
        }, TimeSpan.FromSeconds(2));

        scanned.ShouldBeTrue();
        await scanner.Received(1).LoadFilesFromFolder("/music/library", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartScan_ScannerThrows_StillReleasesLock()
    {
        using var fixture = new BizTestFixture();
        fixture.SeedMonitoredFolder("/music");
        var (service, scanner, _) = BuildService(fixture);
        scanner.LoadFilesFromFolder(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        service.TryStartScan().ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
    }

    [Fact]
    public void TryStartCsvScan_FirstCall_ReturnsTrue()
    {
        using var fixture = new BizTestFixture();
        var (service, _, _) = BuildService(fixture);

        var result = service.TryStartCsvScan("/music/import.csv");

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartCsvScan_WhileScanInProgress_ReturnsFalse()
    {
        using var fixture = new BizTestFixture();
        var (service, scanner, _) = BuildService(fixture);

        var blockGate = new TaskCompletionSource();
        scanner.LoadFilesFromCsv(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => blockGate.Task);

        service.TryStartCsvScan("/music/import.csv").ShouldBeTrue();

        var scannerWasInvoked = await WaitUntil(() => scanner.ReceivedCalls().Any(), TimeSpan.FromSeconds(2));
        scannerWasInvoked.ShouldBeTrue();

        service.TryStartCsvScan("/music/import.csv").ShouldBeFalse();
        service.TryStartScan().ShouldBeFalse();

        blockGate.SetResult();
    }

    [Fact]
    public async Task TryStartCsvScan_CallsScannerWithGivenPath_AndReleasesLockAfterCompletion()
    {
        using var fixture = new BizTestFixture();
        var (service, scanner, _) = BuildService(fixture);
        scanner.LoadFilesFromCsv(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        service.TryStartCsvScan("/music/import.csv").ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
        await scanner.Received(1).LoadFilesFromCsv("/music/import.csv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartCsvScan_ScannerThrows_StillReleasesLock()
    {
        using var fixture = new BizTestFixture();
        var (service, scanner, _) = BuildService(fixture);
        scanner.LoadFilesFromCsv(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        service.TryStartCsvScan("/music/import.csv").ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
    }

    [Fact]
    public void TryStartItunesXmlImport_FirstCall_ReturnsTrue()
    {
        using var fixture = new BizTestFixture();
        var (service, _, _) = BuildService(fixture);

        var result = service.TryStartItunesXmlImport("/music/Library.xml", userId: 1, importMissingTracks: false);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartItunesXmlImport_WhileScanInProgress_ReturnsFalse()
    {
        using var fixture = new BizTestFixture();
        var (service, _, importer) = BuildService(fixture);

        var blockGate = new TaskCompletionSource();
        importer.Import(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => blockGate.Task);

        service.TryStartItunesXmlImport("/music/Library.xml", 1, false).ShouldBeTrue();

        var importerWasInvoked = await WaitUntil(() => importer.ReceivedCalls().Any(), TimeSpan.FromSeconds(2));
        importerWasInvoked.ShouldBeTrue();

        service.TryStartItunesXmlImport("/music/Library.xml", 1, false).ShouldBeFalse();
        service.TryStartScan().ShouldBeFalse();

        blockGate.SetResult();
    }

    [Fact]
    public async Task TryStartItunesXmlImport_CallsImporterWithGivenArguments_AndReleasesLockAfterCompletion()
    {
        using var fixture = new BizTestFixture();
        var (service, _, importer) = BuildService(fixture);
        importer.Import(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        service.TryStartItunesXmlImport("/music/Library.xml", 7, true).ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
        await importer.Received(1).Import("/music/Library.xml", 7, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartItunesXmlImport_ImporterThrows_StillReleasesLock()
    {
        using var fixture = new BizTestFixture();
        var (service, _, importer) = BuildService(fixture);
        importer.Import(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        service.TryStartItunesXmlImport("/music/Library.xml", 1, false).ShouldBeTrue();

        var canStartAgain = await WaitUntil(() => service.TryStartScan(), TimeSpan.FromSeconds(2));

        canStartAgain.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartFolderScan_ScansThatFolderAndReportsItAsCurrentWhileRunning()
    {
        using var fixture = new BizTestFixture();
        var (service, scanner, _) = BuildService(fixture);
        var blockGate = new TaskCompletionSource();
        scanner.LoadFilesFromFolder("/adhoc", Arg.Any<CancellationToken>()).Returns(_ => blockGate.Task);

        service.TryStartFolderScan("/adhoc").ShouldBeTrue();

        (await WaitUntil(() => service.CurrentFolder == "/adhoc", TimeSpan.FromSeconds(2))).ShouldBeTrue();
        service.TryStartFolderScan("/other").ShouldBeFalse();
        blockGate.SetResult();

        (await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(2))).ShouldBeTrue();
        service.CurrentFolder.ShouldBeNull();
        await scanner.Received(1).LoadFilesFromFolder("/adhoc", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TryStartFolderScan_BlankPath_Throws(string path)
    {
        using var fixture = new BizTestFixture();
        var (service, _, _) = BuildService(fixture);

        Should.Throw<ArgumentException>(() => service.TryStartFolderScan(path));
        service.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task TryStartScan_WhenLoadingFoldersFails_StillReleasesLock()
    {
        using var fixture = new BizTestFixture();
        var failingFactory = Substitute.For<IDbContextFactory<PlaatContext>>();
        failingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlaatContext>(new InvalidOperationException("db down")));
        var service = new MonitoredFolderScanService(
            failingFactory,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<MonitoredFolderScanService>>());

        service.TryStartScan().ShouldBeTrue();

        (await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(2))).ShouldBeTrue();
    }
}
