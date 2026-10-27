using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

/// <summary>
/// Covers the filter that every endpoint group adds: what reaches the log, and - just as
/// important - that the status code and body the client sees are unchanged by it.
/// </summary>
public sealed class ExceptionLoggingEndpointFilterTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    private readonly CapturingLoggerProvider _logs = new();

    [Fact]
    public async Task UncaughtException_IsLoggedWithItsStackTraceAndAnsweredWithAPlain500()
    {
        factory.MonitoredFolderRepository.GetAll(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the monitored folders table is gone"));

        var response = await CreateClient().GetAsync("/api/monitored-folders");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        // The exception detail belongs in the log, not in the client's hands.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("the monitored folders table is gone");

        var entry = FilterEntries().ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldContain("GET /api/monitored-folders");
        entry.Exception.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe("the monitored folders table is gone");
    }

    [Fact]
    public async Task BadRequest_KeepsItsStatusAndMessageAndIsLoggedAsAWarning()
    {
        var response = await CreateClient().PostAsJsonAsync(
            "/api/monitored-folders", new AddMonitoredFolderRequest("  "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Path is required.");

        var entry = FilterEntries().ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("POST /api/monitored-folders");
        entry.Message.ShouldContain("400");
        entry.Message.ShouldContain("Path is required.");
    }

    [Fact]
    public async Task SuccessfulResponse_IsNotLogged()
    {
        factory.MonitoredFolderRepository.GetAll(Arg.Any<CancellationToken>())
            .Returns([new MonitoredFolderDto(1, "/music", null)]);

        var response = await CreateClient().GetAsync("/api/monitored-folders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        FilterEntries().ShouldBeEmpty();
    }

    [Fact]
    public async Task OtherErrorStatuses_PassThroughWithoutALogEntry()
    {
        factory.MonitoredFolderRepository.AddFolder("/music/dupe", Arg.Any<CancellationToken>())
            .Throws(new DuplicateNameException());

        var response = await CreateClient().PostAsJsonAsync(
            "/api/monitored-folders", new AddMonitoredFolderRequest("/music/dupe"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        FilterEntries().ShouldBeEmpty();
    }

    private HttpClient CreateClient()
    {
        return factory
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(_logs)))
            .CreateClient();
    }

    private IReadOnlyCollection<CapturedLogEntry> FilterEntries()
    {
        return _logs.Entries
            .Where(entry => entry.Category == typeof(ExceptionLoggingEndpointFilter).FullName)
            .ToList();
    }

    private sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, Entries);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLogEntry(categoryName, logLevel, formatter(state, exception), exception));
        }
    }
}
