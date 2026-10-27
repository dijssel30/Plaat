namespace Plaat.Db.Models;

public enum ArtistDiscogsLookupStatus
{
    /// <summary>
    /// No lookup has been attempted for this artist yet.
    /// </summary>
    NotAttempted,

    /// <summary>
    /// A lookup has been claimed by the enrichment queue and is currently running.
    /// </summary>
    InProgress,

    /// <summary>
    /// A matching Discogs artist was found and its details were saved.
    /// </summary>
    Found,

    /// <summary>
    /// No exact-name match was found on Discogs. Will not be retried automatically.
    /// </summary>
    NotFound
}
