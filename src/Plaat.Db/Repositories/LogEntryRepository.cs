using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for persisted application log entries.
/// </summary>
public interface ILogEntryRepository
{
    /// <summary>
    /// Persists a batch of log entries.
    /// </summary>
    Task AddRange(IReadOnlyCollection<LogEntry> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recent log entries matching <paramref name="query"/>, newest first.
    /// </summary>
    Task<IReadOnlyCollection<LogEntryDto>> GetRecent(LogQuery query, CancellationToken cancellationToken);
}

internal sealed class LogEntryRepository(IDbContextFactory<PlaatContext> dbContextFactory) : ILogEntryRepository
{
    private const string LikeEscapeChar = "\\";

    public async Task AddRange(IReadOnlyCollection<LogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count is 0)
        {
            return;
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        context.AddRange(entries);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<LogEntryDto>> GetRecent(LogQuery query, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<LogEntry> filtered = context.LogEntries;

        if (query.Levels is { Count: > 0 })
        {
            // Unparseable names deliberately match nothing rather than being ignored, so a
            // filter never returns more than what the caller asked for.
            var levels = ParseLevels(query.Levels);

            filtered = filtered.Where(e => levels.Contains(e.Level));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // LIKE rather than Contains: SQLite's LIKE is case-insensitive for ASCII, whereas
            // Contains(..., StringComparison.OrdinalIgnoreCase) has no SQL translation.
            var pattern = $"%{EscapeForLike(query.Search.Trim())}%";

            filtered = filtered.Where(e => EF.Functions.Like(e.Message, pattern, LikeEscapeChar));
        }

        var entries = await filtered
            .OrderByDescending(e => e.Timestamp)
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. entries
                .Select(e => new LogEntryDto(e.Id, e.Timestamp, e.Level.ToString(), e.Category, e.Message, e.Exception))
        ];
    }

    private static LogLevel[] ParseLevels(IReadOnlyCollection<string> levels)
    {
        return
        [
            .. levels
                .Select(l => Enum.TryParse<LogLevel>(l, true, out var level) ? level : (LogLevel?)null)
                .Where(l => l is not null)
                .Select(l => l!.Value)
                .Distinct()
        ];
    }

    private static string EscapeForLike(string value)
    {
        return value
            .Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
            .Replace("%", LikeEscapeChar + "%")
            .Replace("_", LikeEscapeChar + "_");
    }
}
