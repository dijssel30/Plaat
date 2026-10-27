using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class LogEntryRepositoryTests
{
    [Fact]
    public async Task AddRange_EmptyCollection_DoesNotHitDatabase()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        await repo.AddRange([], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Limit: 10), CancellationToken.None);
        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddRange_ThenGetRecent_ReturnsInsertedEntries()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        await repo.AddRange([
            new LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = LogLevel.Warning,
                Category = "Plaat.Biz.ConvertQueueService",
                Message = "Something happened"
            }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Limit: 10), CancellationToken.None);

        entries.ShouldHaveSingleItem();
        entries.Single().Level.ShouldBe(nameof(LogLevel.Warning));
        entries.Single().Category.ShouldBe("Plaat.Biz.ConvertQueueService");
        entries.Single().Message.ShouldBe("Something happened");
    }

    [Fact]
    public async Task GetRecent_OrdersByTimestampDescending()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-2), Level = LogLevel.Warning, Category = "A", Message = "oldest" },
            new LogEntry { Timestamp = now, Level = LogLevel.Error, Category = "B", Message = "newest" },
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Warning, Category = "C", Message = "middle" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Limit: 10), CancellationToken.None);

        entries.Select(e => e.Message).ShouldBe(["newest", "middle", "oldest"]);
    }

    [Fact]
    public async Task GetRecent_RespectsLimit()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-2), Level = LogLevel.Warning, Category = "A", Message = "1" },
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Warning, Category = "A", Message = "2" },
            new LogEntry { Timestamp = now, Level = LogLevel.Warning, Category = "A", Message = "3" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Limit: 2), CancellationToken.None);

        entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddRange_PreservesException()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        await repo.AddRange([
            new LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = LogLevel.Error,
                Category = "A",
                Message = "boom",
                Exception = "System.Exception: boom"
            }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Limit: 10), CancellationToken.None);

        entries.Single().Exception.ShouldBe("System.Exception: boom");
    }

    [Fact]
    public async Task GetRecent_FiltersByLevel()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-2), Level = LogLevel.Warning, Category = "A", Message = "warn" },
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Error, Category = "A", Message = "err" },
            new LogEntry { Timestamp = now, Level = LogLevel.Critical, Category = "A", Message = "crit" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(["error", "Critical"], Limit: 10), CancellationToken.None);

        entries.Select(e => e.Message).ShouldBe(["crit", "err"]);
    }

    [Fact]
    public async Task GetRecent_UnknownLevel_MatchesNothing()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        await repo.AddRange([
            new LogEntry { Timestamp = DateTimeOffset.UtcNow, Level = LogLevel.Warning, Category = "A", Message = "warn" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(["Nonsense"], Limit: 10), CancellationToken.None);

        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRecent_FiltersByMessageCaseInsensitively()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Warning, Category = "A", Message = "Could not convert item" },
            new LogEntry { Timestamp = now, Level = LogLevel.Error, Category = "B", Message = "Scan failed" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Search: "convert", Limit: 10), CancellationToken.None);

        entries.ShouldHaveSingleItem();
        entries.Single().Message.ShouldBe("Could not convert item");
    }

    [Fact]
    public async Task GetRecent_SearchTreatsWildcardsAsLiterals()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Warning, Category = "A", Message = "100% failed" },
            new LogEntry { Timestamp = now, Level = LogLevel.Warning, Category = "A", Message = "nothing to see" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(Search: "%", Limit: 10), CancellationToken.None);

        entries.ShouldHaveSingleItem();
        entries.Single().Message.ShouldBe("100% failed");
    }

    [Fact]
    public async Task GetRecent_CombinesLevelAndSearchFilters()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateLogEntryRepository();

        var now = DateTimeOffset.UtcNow;

        await repo.AddRange([
            new LogEntry { Timestamp = now.AddMinutes(-2), Level = LogLevel.Warning, Category = "A", Message = "scan failed" },
            new LogEntry { Timestamp = now.AddMinutes(-1), Level = LogLevel.Error, Category = "A", Message = "scan failed" },
            new LogEntry { Timestamp = now, Level = LogLevel.Error, Category = "A", Message = "convert failed" }
        ], CancellationToken.None);

        var entries = await repo.GetRecent(new LogQuery(["Error"], "scan", 10), CancellationToken.None);

        entries.ShouldHaveSingleItem();
        entries.Single().Level.ShouldBe(nameof(LogLevel.Error));
    }
}
