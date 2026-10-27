namespace Plaat.Contract.Models;

/// <summary>
/// A single page of a larger result set, plus the total count so a virtualized UI knows how far
/// it can scroll without fetching every page.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Total number of items matching the query, across all pages.
    /// </summary>
    public int TotalCount { get; init; }
}
