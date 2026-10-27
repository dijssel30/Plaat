using Microsoft.Extensions.Logging;

namespace Plaat.Db.Models;

public class LogEntry
{
    public uint Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required LogLevel Level { get; init; }

    public required string Category { get; init; }

    public required string Message { get; init; }

    public string? Exception { get; init; }
}
