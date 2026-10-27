namespace Plaat.Contract.Models;

/// <summary>
/// Whose plays a listening-stats request counts.
/// </summary>
public enum StatsScope
{
    /// <summary>
    /// Only the calling user's own plays.
    /// </summary>
    Me,

    /// <summary>
    /// Every play in the library, including the ones recorded before plays were attributed to a
    /// user (those have no user at all, so they are invisible under <see cref="Me"/>).
    /// </summary>
    Everyone
}
