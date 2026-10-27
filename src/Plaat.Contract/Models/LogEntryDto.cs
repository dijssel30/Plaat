namespace Plaat.Contract.Models;

public sealed record LogEntryDto(
    uint Id,
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception);
