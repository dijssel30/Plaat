using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.Workers;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class SmartPlaylistCountServiceTests
{
    private readonly IPlaylistRepository _repository = Substitute.For<IPlaylistRepository>();
    private readonly SmartPlaylistCountService _service;

    public SmartPlaylistCountServiceTests()
    {
        _service = new SmartPlaylistCountService(_repository, NullLogger<SmartPlaylistCountService>.Instance);
    }

    [Fact]
    public async Task RecalculateAll_DelegatesToRepositoryAndEndsIdle()
    {
        await _service.RecalculateAll(CancellationToken.None);

        await _repository.Received(1).RecalculateAllSmartPlaylistTrackCounts(Arg.Any<CancellationToken>());
        _service.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task RecalculateAll_WhileARunIsInProgress_IsSkipped()
    {
        var release = new TaskCompletionSource();
        _repository.RecalculateAllSmartPlaylistTrackCounts(Arg.Any<CancellationToken>()).Returns(release.Task);

        var first = _service.RecalculateAll(CancellationToken.None);
        _service.IsBusy.ShouldBeTrue();
        await _service.RecalculateAll(CancellationToken.None);
        release.SetResult();
        await first;

        await _repository.Received(1).RecalculateAllSmartPlaylistTrackCounts(Arg.Any<CancellationToken>());
        _service.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task RecalculateAll_WhenRepositoryFails_SwallowsAndBecomesIdle()
    {
        _repository.RecalculateAllSmartPlaylistTrackCounts(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        await Should.NotThrowAsync(() => _service.RecalculateAll(CancellationToken.None));

        _service.IsBusy.ShouldBeFalse();
    }
}
