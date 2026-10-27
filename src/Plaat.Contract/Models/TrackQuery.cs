namespace Plaat.Contract.Models;

/// <summary>
/// Filter / sort / paging parameters for a server-side track listing, so browsing a 100k-track
/// library never transfers more than one screenful at a time.
/// </summary>
/// <param name="Search">Optional free-text filter, matched the same way the client used to match locally.</param>
/// <param name="Sort">Column to order by.</param>
/// <param name="Descending">Order direction.</param>
/// <param name="Skip">Number of matching rows to skip (page offset).</param>
/// <param name="Take">Page size. 0 or negative means "all matching rows" (used only for building a play queue).</param>
public sealed record TrackQuery(
    string? Search = null,
    TrackSortField Sort = TrackSortField.Title,
    bool Descending = false,
    int Skip = 0,
    int Take = 100);
