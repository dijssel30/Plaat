namespace Plaat.Contract.Models;

/// <summary>
/// Filter parameters for the admin log listing.
/// </summary>
/// <param name="Levels">
/// Log level names to include (case-insensitive). Empty or <c>null</c> means "every level".
/// </param>
/// <param name="Search">Optional free-text filter, matched against the log message.</param>
/// <param name="Limit">Maximum number of (newest-first) entries to return.</param>
public sealed record LogQuery(
    IReadOnlyCollection<string>? Levels = null,
    string? Search = null,
    int Limit = 200);
