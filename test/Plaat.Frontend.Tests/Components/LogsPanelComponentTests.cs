using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Pages.AdminPanels;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class LogsPanelComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public LogsPanelComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
    }

    [Fact]
    public void OnInit_LoadsLogsFromClient()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([
                new LogEntryDto(1, DateTimeOffset.UtcNow, "Warning", "Plaat.Biz.ConvertQueueService", "Could not convert item", null),
                new LogEntryDto(2, DateTimeOffset.UtcNow, "Error", "Plaat.Biz.MusicScanner", "Scan failed", "System.Exception: boom")
            ]));

        var cut = Render<LogsPanel>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            rows.Count.ShouldBe(2);
        });
    }

    [Fact]
    public void OnInit_NoEntries_ShowsEmptyMessage()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([]));

        var cut = Render<LogsPanel>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No warnings or errors logged.");
        });
    }

    [Fact]
    public void OnInit_ClientThrows_ShowsErrorMessage()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyCollection<LogEntryDto>>(_ => throw new HttpRequestException("network down"));

        var cut = Render<LogsPanel>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Failed to load logs");
        });
    }

    [Fact]
    public void EntryWithException_RendersExceptionDetails()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([
                new LogEntryDto(1, DateTimeOffset.UtcNow, "Error", "Plaat.Biz.MusicScanner", "Scan failed", "System.Exception: boom")
            ]));

        var cut = Render<LogsPanel>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("System.Exception: boom");
        });
    }

    [Fact]
    public void ToggleLevel_ReloadsWithThatLevelOnly()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([]));

        var cut = Render<LogsPanel>();

        cut.FindAll(".level-filters button").Single(b => b.TextContent.Trim() == "Error").Click();

        cut.WaitForAssertion(() =>
            _client.Received().GetLogs(
                Arg.Is<LogQuery>(q => q!.Levels != null && q.Levels.SequenceEqual(new[] { "Error" })),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ToggleLevelTwice_ClearsTheLevelFilter()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([]));

        var cut = Render<LogsPanel>();

        var error = cut.FindAll(".level-filters button").Single(b => b.TextContent.Trim() == "Error");
        error.Click();
        cut.FindAll(".level-filters button").Single(b => b.TextContent.Trim() == "Error").Click();

        cut.WaitForAssertion(() =>
            _client.Received().GetLogs(
                Arg.Is<LogQuery>(q => q!.Levels == null),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void TypingSearch_ReloadsWithSearchTerm()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([]));

        var cut = Render<LogsPanel>();

        cut.Find(".log-search").Input("scan");

        cut.WaitForAssertion(
            () => _client.Received().GetLogs(
                Arg.Is<LogQuery>(q => q!.Search == "scan"),
                Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NoResultsWithFilterApplied_ShowsFilteredEmptyMessage()
    {
        _client.GetLogs(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LogEntryDto>>([]));

        var cut = Render<LogsPanel>();

        cut.FindAll(".level-filters button").Single(b => b.TextContent.Trim() == "Critical").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No log entries match the current filters."));
    }
}
