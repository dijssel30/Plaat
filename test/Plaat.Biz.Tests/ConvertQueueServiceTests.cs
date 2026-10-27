using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Biz.Qaac;
using Plaat.Biz.Tests.Helpers;
using Plaat.Biz.Workers;
using Plaat.Db;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class ConvertQueueServiceTests
{
    private static PlaatOptions BuildOptions(string targetReconvertedPath)
    {
        return new()
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = targetReconvertedPath,
            JwtOptions = new JwtOptions { Key = "test-key" }
        };
    }

    private static ConvertQueueService BuildService(BizTestFixture fixture, string targetReconvertedPath)
    {
        var logger = Substitute.For<ILogger<ConvertQueueService>>();
        var qaacLogger = Substitute.For<ILogger<QaacHelper>>();
        var qaacHelper = new QaacHelper(qaacLogger, new PlaatOptions
        {
            QaacPath = "qaac.exe",
            ImagesStoragePath = "",
            TargetReconvertedPath = "",
            JwtOptions = new JwtOptions { Key = "test-key" }
        });
        return new ConvertQueueService(fixture.DbFactory, logger, BuildOptions(targetReconvertedPath), qaacHelper);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task ConvertItem_EmptyQueue_ReturnsFalse()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = BuildService(fixture, tempDir);

            var result = await service.ConvertItem(CancellationToken.None);

            result.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItem_OutputAlreadyExists_RemovesQueueItemAndAddsSupportedLocation()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string fileHash = "abc123";
            var item = fixture.SeedConvertQueueItem("/music/original.alac", fileHash);

            var outputPath = Path.Join(tempDir, fileHash) + ".m4a";
            await File.WriteAllTextAsync(outputPath, "fake converted audio");

            var service = BuildService(fixture, tempDir);

            var result = await service.ConvertItem(CancellationToken.None);

            result.ShouldBeTrue();

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.AnyAsync(i => i.Id == item.Id)).ShouldBeFalse();

            var track = await context.Tracks
                .Include(t => t.TrackLocations)
                .FirstAsync(t => t.Id == item.TrackLocation.TrackId);

            track.TrackLocations.ShouldContain(l => l.Path == outputPath && l.IsSupportedFormat);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItems_MultipleItemsInQueue_ProcessesAll()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var item1 = fixture.SeedConvertQueueItem("/music/one.alac", "hash1");
            var item2 = fixture.SeedConvertQueueItem("/music/two.alac", "hash2");

            await File.WriteAllTextAsync(Path.Join(tempDir, "hash1") + ".m4a", "fake");
            await File.WriteAllTextAsync(Path.Join(tempDir, "hash2") + ".m4a", "fake");

            var service = BuildService(fixture, tempDir);

            await service.ConvertItems(CancellationToken.None);

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.CountAsync()).ShouldBe(0);
            (await context.Tracks.CountAsync(t =>
                t.Id == item1.TrackLocation.TrackId || t.Id == item2.TrackLocation.TrackId)).ShouldBe(2);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItems_ItemStuckWorking_DoesNotCountTowardsQueueAndReturnsFalse()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            fixture.SeedConvertQueueItem("/music/stuck.alac", "stuckhash", working: true);

            var service = BuildService(fixture, tempDir);

            var result = await service.ConvertItems(CancellationToken.None);

            result.ShouldBeFalse();

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.CountAsync()).ShouldBe(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Construction_ItemLeftStuckWorkingFromPreviousRun_IsResetAndPickedUpAgain()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string fileHash = "abc123";
            fixture.SeedConvertQueueItem("/music/original.alac", fileHash, working: true);
            await File.WriteAllTextAsync(Path.Join(tempDir, fileHash) + ".m4a", "fake converted audio");

            var service = BuildService(fixture, tempDir);

            var resetHappened = await WaitUntil(
                () =>
                {
                    using var context = fixture.DbFactory.CreateDbContext();
                    return !context.ConvertQueueItems.Any(i => i.Working);
                },
                TimeSpan.FromSeconds(2));

            resetHappened.ShouldBeTrue();

            var result = await service.ConvertItem(CancellationToken.None);

            result.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryStartConvert_FirstCall_ReturnsTrue()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = BuildService(fixture, tempDir);

            var result = service.TryStartConvert();

            result.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TryStartConvert_AfterRunCompletes_IsNotBusyAndCanStartAgain()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string fileHash = "abc123";
            fixture.SeedConvertQueueItem("/music/original.alac", fileHash);
            await File.WriteAllTextAsync(Path.Join(tempDir, fileHash) + ".m4a", "fake converted audio");

            var service = BuildService(fixture, tempDir);

            service.TryStartConvert().ShouldBeTrue();

            var idleAgain = await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(2));
            idleAgain.ShouldBeTrue();

            service.TryStartConvert().ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItems_ConversionAlwaysFails_StopsAfterMaxAttemptsAndKeepsItemQueued()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var item = fixture.SeedConvertQueueItem(Path.Join(tempDir, "does-not-exist.alac"), "badhash");
            var service = BuildService(fixture, tempDir);

            var iterations = 0;
            while (await service.ConvertItems(CancellationToken.None))
            {
                iterations++;
                iterations.ShouldBeLessThanOrEqualTo(ConvertQueueService.MaxAttempts);
            }

            iterations.ShouldBe(ConvertQueueService.MaxAttempts);

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            var stored = await context.ConvertQueueItems.SingleAsync(i => i.Id == item.Id);
            stored.Attempts.ShouldBe(ConvertQueueService.MaxAttempts);
            stored.Working.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItem_ItemReachedMaxAttempts_IsSkipped()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string fileHash = "abc123";
            fixture.SeedConvertQueueItem("/music/original.alac", fileHash, attempts: ConvertQueueService.MaxAttempts);
            await File.WriteAllTextAsync(Path.Join(tempDir, fileHash) + ".m4a", "fake converted audio");

            var service = BuildService(fixture, tempDir);

            (await service.ConvertItem(CancellationToken.None)).ShouldBeFalse();

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.CountAsync()).ShouldBe(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItem_ItemWithPreviousFailures_StillConvertsBelowMax()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string fileHash = "abc123";
            fixture.SeedConvertQueueItem("/music/original.alac", fileHash, attempts: ConvertQueueService.MaxAttempts - 1);
            await File.WriteAllTextAsync(Path.Join(tempDir, fileHash) + ".m4a", "fake converted audio");

            var service = BuildService(fixture, tempDir);

            (await service.ConvertItem(CancellationToken.None)).ShouldBeTrue();

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.CountAsync()).ShouldBe(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TryStartConvert_ConversionAlwaysFails_BecomesIdleAgain()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            fixture.SeedConvertQueueItem(Path.Join(tempDir, "does-not-exist.alac"), "badhash");
            var service = BuildService(fixture, tempDir);

            service.TryStartConvert().ShouldBeTrue();

            (await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(30))).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ConvertQueueService BuildService(
        IDbContextFactory<PlaatContext> factory,
        string targetReconvertedPath,
        bool useQaac = false)
    {
        var options = new PlaatOptions
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = targetReconvertedPath,
            JwtOptions = new JwtOptions { Key = "test-key" },
            UseQaac = useQaac,
            QaacPath = "/nonexistent/qaac.exe"
        };

        return new ConvertQueueService(
            factory,
            NullLogger<ConvertQueueService>.Instance,
            options,
            new QaacHelper(NullLogger<QaacHelper>.Instance, options));
    }

    [Fact]
    public async Task ConvertItem_RealLosslessFile_IsTranscodedToAacAndGetsASupportedLocation()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var source = await AudioFiles.Create(Path.Join(tempDir, "source.wav"));
            var item = fixture.SeedConvertQueueItem(source, "realhash");
            var service = BuildService(fixture.DbFactory, tempDir);

            (await service.ConvertItem(CancellationToken.None)).ShouldBeTrue();

            var outputPath = Path.Join(tempDir, "realhash") + ".m4a";
            File.Exists(outputPath).ShouldBeTrue();
            File.Exists(Path.Join(tempDir, "realhash") + ".tmp.m4a").ShouldBeFalse();
            var mediaInfo = await new MediaInfoParser(NullLogger<MediaInfoParser>.Instance).GetMediaInfoResult(outputPath);
            mediaInfo!.Media!.Tracks.ShouldContain(t => t.Type == "Audio" && t.Format == "AAC");

            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            (await context.ConvertQueueItems.AnyAsync(i => i.Id == item.Id)).ShouldBeFalse();
            (await context.TrackLocations.SingleAsync(l => l.Path == outputPath)).IsSupportedFormat.ShouldBeTrue();
            service.CurrentItems.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertItem_UsingQaacThatIsNotInstalled_RequeuesWithAnAttempt()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var source = await AudioFiles.Create(Path.Join(tempDir, "source.wav"));
            var item = fixture.SeedConvertQueueItem(source, "qaachash");
            var service = BuildService(fixture.DbFactory, tempDir, useQaac: true);

            (await service.ConvertItem(CancellationToken.None)).ShouldBeTrue();

            File.Exists(Path.Join(tempDir, "qaachash") + ".m4a").ShouldBeFalse();
            await using var context = await fixture.DbFactory.CreateDbContextAsync();
            var stored = await context.ConvertQueueItems.SingleAsync(i => i.Id == item.Id);
            stored.Attempts.ShouldBe(1);
            stored.Working.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Construction_WhenStaleResetFails_ProcessingLoopStillRuns()
    {
        using var fixture = new BizTestFixture();
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var calls = 0;
            var factory = Substitute.For<IDbContextFactory<PlaatContext>>();
            factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                Interlocked.Increment(ref calls) == 1
                    ? Task.FromException<PlaatContext>(new InvalidOperationException("db not ready"))
                    : fixture.DbFactory.CreateDbContextAsync());
            var service = BuildService(factory, tempDir);

            (await WaitUntil(() => Volatile.Read(ref calls) >= 1, TimeSpan.FromSeconds(10))).ShouldBeTrue();
            service.TryStartConvert().ShouldBeTrue();

            (await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(10))).ShouldBeTrue();
            calls.ShouldBeGreaterThan(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TryStartConvert_WhenRunThrows_BecomesIdleAgain()
    {
        var factory = Substitute.For<IDbContextFactory<PlaatContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlaatContext>(new InvalidOperationException("db down")));
        var service = BuildService(factory, Path.GetTempPath());

        service.TryStartConvert().ShouldBeTrue();

        (await WaitUntil(() => !service.IsBusy, TimeSpan.FromSeconds(10))).ShouldBeTrue();
    }
}
