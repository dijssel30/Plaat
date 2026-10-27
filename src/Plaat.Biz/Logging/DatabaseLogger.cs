using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Plaat.Db.Models;

namespace Plaat.Biz.Logging;

internal sealed class DatabaseLogger(string categoryName, ChannelWriter<LogEntry> writer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Warning;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        writer.TryWrite(new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = logLevel,
            Category = categoryName,
            Message = formatter(state, exception),
            Exception = exception?.ToString()
        });
    }
}
