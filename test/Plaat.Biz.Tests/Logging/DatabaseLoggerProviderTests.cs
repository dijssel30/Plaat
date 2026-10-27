using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Biz.Logging;
using Plaat.Db.Models;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests.Logging;

public sealed class DatabaseLoggerProviderTests
{
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
    public void CreateLogger_SameCategoryTwice_ReturnsSameInstance()
    {
        var repository = Substitute.For<ILogEntryRepository>();
        using var provider = new DatabaseLoggerProvider(repository);

        var first = provider.CreateLogger("SameCategory");
        var second = provider.CreateLogger("SameCategory");

        first.ShouldBeSameAs(second);
    }

    [Fact]
    public async Task Log_WarningEntry_IsEventuallyPersistedViaRepository()
    {
        var repository = Substitute.For<ILogEntryRepository>();
        var persisted = new List<LogEntry>();

        repository.AddRange(Arg.Any<IReadOnlyCollection<LogEntry>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => persisted.AddRange(ci.Arg<IReadOnlyCollection<LogEntry>>()!));

        using var provider = new DatabaseLoggerProvider(repository);
        var logger = provider.CreateLogger("Plaat.Biz.MusicScanner");

        logger.LogWarning("Something needs attention");

        var received = await WaitUntil(() => persisted.Count == 1, TimeSpan.FromSeconds(2));

        received.ShouldBeTrue();
        persisted.Single().Category.ShouldBe("Plaat.Biz.MusicScanner");
        persisted.Single().Message.ShouldBe("Something needs attention");
    }

    [Fact]
    public void Log_DoesNotThrow_WhenRepositoryWriteFails()
    {
        var repository = Substitute.For<ILogEntryRepository>();

        repository.AddRange(Arg.Any<IReadOnlyCollection<LogEntry>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db unavailable"));

        using var provider = new DatabaseLoggerProvider(repository);
        var logger = provider.CreateLogger("Plaat.Biz.MusicScanner");

        Should.NotThrow(() => logger.LogWarning("Something needs attention"));
    }

    [Fact]
    public async Task Log_BacklogLargerThanABatch_IsPersistedInCappedBatches()
    {
        var repository = Substitute.For<ILogEntryRepository>();
        var release = new TaskCompletionSource();
        var batchSizes = new List<int>();
        repository.AddRange(Arg.Any<IReadOnlyCollection<LogEntry>>(), Arg.Any<CancellationToken>())
            .Returns(_ => batchSizes.Count == 1 ? release.Task : Task.CompletedTask)
            .AndDoes(ci => batchSizes.Add(ci.Arg<IReadOnlyCollection<LogEntry>>()!.Count));

        using var provider = new DatabaseLoggerProvider(repository);
        var logger = provider.CreateLogger("Backlog");

        logger.LogWarning("first");
        (await WaitUntil(() => batchSizes.Count == 1, TimeSpan.FromSeconds(2))).ShouldBeTrue();
        for (var i = 0; i < 450; i++)
        {
            logger.LogWarning("queued {Index}", i);
        }

        release.SetResult();

        (await WaitUntil(() => batchSizes.Sum() == 451, TimeSpan.FromSeconds(5))).ShouldBeTrue();
        batchSizes.ShouldAllBe(size => size <= 200);
        batchSizes.ShouldContain(200);
    }
}
