using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Plaat.Db.Models;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Logging;

/// <summary>
/// Logs Warning-and-above entries to the database on a background loop.
/// </summary>
public sealed class DatabaseLoggerProvider : ILoggerProvider
{
    private const int MaxBatchSize = 200;

    private readonly Channel<LogEntry> _entries = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();
    private readonly ILogEntryRepository _repository;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _processingLoop;

    public DatabaseLoggerProvider(ILogEntryRepository repository)
    {
        _repository = repository;
        _processingLoop = Task.Run(() => ProcessEntriesAsync(CancellationToken.None));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(name, _entries.Writer));
    }

    private async Task ProcessEntriesAsync(CancellationToken cancellationToken)
    {
        var batch = new List<LogEntry>(MaxBatchSize);

        await foreach (var entry in _entries.Reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(entry);

            while (batch.Count < MaxBatchSize && _entries.Reader.TryRead(out var next))
            {
                batch.Add(next);
            }

            try
            {
                await _repository.AddRange(batch, cancellationToken);
            }
            catch
            {
                // Swallowed deliberately: this is the log sink itself, so a DB failure here has
                // nowhere else to go without risking a loop back into this provider. Console
                // logging (registered alongside this provider) is unaffected and remains
                // available for diagnosing DB issues.
            }

            batch.Clear();
        }
    }

    public void Dispose()
    {
        _entries.Writer.TryComplete();
    }
}
