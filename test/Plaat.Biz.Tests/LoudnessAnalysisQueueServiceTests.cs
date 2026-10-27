using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Biz.Workers;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class LoudnessAnalysisQueueServiceTests : IDisposable
{
    private readonly ITrackRepository _repository = Substitute.For<ITrackRepository>();
    private readonly LoudnessAnalysisQueueService _service;

    public LoudnessAnalysisQueueServiceTests()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ITrackRepository)).Returns(_repository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _service = new LoudnessAnalysisQueueService(
            scopeFactory,
            new LoudnessAnalyzer(NullLogger<LoudnessAnalyzer>.Instance),
            new PlaatOptions
            {
                ImagesStoragePath = "x",
                TargetReconvertedPath = "x",
                JwtOptions = new JwtOptions { Key = "k" },
                LoudnessAnalysisMaxDegreeOfParallelism = 0
            },
            NullLogger<LoudnessAnalysisQueueService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    private async Task WaitUntilIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_service.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        _service.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task TryStartAnalysis_NothingToAnalyze_FinishesWithoutRefreshingCache()
    {
        _repository.GetTrackIdsNeedingLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns([]);

        _service.TryStartAnalysis().ShouldBeTrue();
        await WaitUntilIdle();

        _service.TotalCount.ShouldBe(0);
        _service.ProcessedCount.ShouldBe(0);
        await _repository.DidNotReceiveWithAnyArgs().RefreshListCache(CancellationToken.None);
    }

    [Fact]
    public async Task TryStartAnalysis_AnalyzesEachTrackToleratingMissingPathsAndFailures()
    {
        _repository.GetTrackIdsNeedingLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns([1u, 2u, 3u]);
        _repository.GetPath(1, Arg.Any<CancellationToken>())
            .Returns(new TrackPath { Id = 1, Path = "/nonexistent/track.mp3", MimeType = "audio/mpeg" });
        _repository.GetPath(2, Arg.Any<CancellationToken>()).Returns((TrackPath?)null);
        _repository.GetPath(3, Arg.Any<CancellationToken>())
            .Returns<TrackPath?>(_ => throw new InvalidOperationException("db error"));

        _service.TryStartAnalysis().ShouldBeTrue();
        await WaitUntilIdle();

        _service.TotalCount.ShouldBe(3);
        _service.ProcessedCount.ShouldBe(3);
        await _repository.Received(1).SetIntegratedLoudness(1, null, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SetIntegratedLoudness(2, Arg.Any<float?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).RefreshListCache(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartAnalysis_WhileRunning_ReturnsFalse()
    {
        var release = new TaskCompletionSource<IReadOnlyCollection<uint>>();
        _repository.GetTrackIdsNeedingLoudnessAnalysis(Arg.Any<CancellationToken>()).Returns(release.Task);

        _service.TryStartAnalysis().ShouldBeTrue();
        _service.TryStartAnalysis().ShouldBeFalse();
        release.SetResult([]);

        await WaitUntilIdle();
    }

    [Fact]
    public async Task TryStartAnalysis_WhenRunFails_BecomesIdleAndCanRunAgain()
    {
        _repository.GetTrackIdsNeedingLoudnessAnalysis(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyCollection<uint>>(new InvalidOperationException("boom")));

        _service.TryStartAnalysis().ShouldBeTrue();
        await WaitUntilIdle();

        _service.TryStartAnalysis().ShouldBeTrue();
        await WaitUntilIdle();
    }
}
