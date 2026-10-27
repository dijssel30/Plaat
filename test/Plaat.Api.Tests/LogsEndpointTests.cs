using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class LogsEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetLogs_ReturnsEntriesFromRepository()
    {
        factory.LogEntryRepository.GetRecent(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new LogEntryDto(1, DateTimeOffset.UtcNow, "Warning", "Plaat.Biz.ConvertQueueService", "Something happened", null),
                new LogEntryDto(2, DateTimeOffset.UtcNow, "Error", "Plaat.Biz.MusicScanner", "Boom", "System.Exception: Boom")
            ]);

        var response = await _client.GetAsync("/api/logs");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<LogEntryDto[]>();
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
    }

    [Fact]
    public async Task GetLogs_PassesLimitFromQueryString()
    {
        // The substitute is shared by the class fixture, so drop calls other tests recorded.
        factory.LogEntryRepository.ClearReceivedCalls();
        factory.LogEntryRepository.GetRecent(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _client.GetAsync("/api/logs?limit=50");

        await factory.LogEntryRepository.Received(1)
            .GetRecent(Arg.Is<LogQuery>(q => q!.Limit == 50), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogs_PassesLevelsAndSearchFromQueryString()
    {
        // The substitute is shared by the class fixture, so drop calls other tests recorded.
        factory.LogEntryRepository.ClearReceivedCalls();
        factory.LogEntryRepository.GetRecent(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _client.GetAsync("/api/logs?levels=Warning&levels=Error&search=scan%20failed");

        await factory.LogEntryRepository.Received(1).GetRecent(
            Arg.Is<LogQuery>(q =>
                q!.Levels != null
                && q.Levels.SequenceEqual(new[] { "Warning", "Error" })
                && q.Search == "scan failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogs_NoFilters_PassesNoLevelsOrSearch()
    {
        // The substitute is shared by the class fixture, so drop calls other tests recorded.
        factory.LogEntryRepository.ClearReceivedCalls();
        factory.LogEntryRepository.GetRecent(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _client.GetAsync("/api/logs");

        await factory.LogEntryRepository.Received(1).GetRecent(
            Arg.Is<LogQuery>(q => (q!.Levels == null || q.Levels.Count == 0) && q.Search == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogs_ClampsExcessiveLimit()
    {
        // The substitute is shared by the class fixture, so drop calls other tests recorded.
        factory.LogEntryRepository.ClearReceivedCalls();
        factory.LogEntryRepository.GetRecent(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _client.GetAsync("/api/logs?limit=100000");

        await factory.LogEntryRepository.Received(1)
            .GetRecent(Arg.Is<LogQuery>(q => q!.Limit == 1000), Arg.Any<CancellationToken>());
    }
}
