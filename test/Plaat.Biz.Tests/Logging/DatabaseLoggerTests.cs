using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Logging;
using Plaat.Db.Models;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests.Logging;

public sealed class DatabaseLoggerTests
{
    private static (DatabaseLogger Logger, ChannelReader<LogEntry> Reader) BuildLogger(string category = "TestCategory")
    {
        var channel = Channel.CreateUnbounded<LogEntry>();
        return (new DatabaseLogger(category, channel.Writer), channel.Reader);
    }

    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, false)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    public void IsEnabled_OnlyWarningAndAbove(LogLevel level, bool expected)
    {
        var (logger, _) = BuildLogger();

        logger.IsEnabled(level).ShouldBe(expected);
    }

    [Fact]
    public void Log_BelowWarning_DoesNotWriteToChannel()
    {
        var (logger, reader) = BuildLogger();

        logger.Log(LogLevel.Information, default, "state", null, (s, _) => s);

        reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Log_WarningOrAbove_WritesEntryWithExpectedFields()
    {
        var (logger, reader) = BuildLogger("Plaat.Biz.ConvertQueueService");
        var exception = new InvalidOperationException("boom");

        logger.Log(LogLevel.Warning, default, "state", exception, (_, ex) => $"Failed: {ex?.Message}");

        reader.TryRead(out var entry).ShouldBeTrue();
        entry!.Category.ShouldBe("Plaat.Biz.ConvertQueueService");
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("Failed: boom");
        entry.Exception.ShouldBe(exception.ToString());
    }

    [Fact]
    public void Log_WithoutException_LeavesExceptionNull()
    {
        var (logger, reader) = BuildLogger();

        logger.Log(LogLevel.Warning, default, "state", null, (s, _) => s);

        reader.TryRead(out var entry).ShouldBeTrue();
        entry!.Exception.ShouldBeNull();
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        var (logger, _) = BuildLogger();

        logger.BeginScope("scope").ShouldBeNull();
    }
}
