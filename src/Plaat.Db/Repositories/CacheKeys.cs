using Plaat.Contract.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Every <see cref="ILibraryCache"/> key used across the repositories, in one place so that
/// cross-repository invalidation (a play-count bump in <see cref="TrackRepository"/> that also has
/// to touch the containing album's / artists' cached detail) cannot drift out of sync with the
/// repository that produces the entry.
/// </summary>
// If you add a new SmartPlaylistRule type, or a new write path that changes data an existing rule
// reads, also invalidate SmartPlaylistTracksPrefix wherever that data changes - see call sites in
// TrackRepository.IncreasePlayCount, TrackRatingRepository.SetRating,
// MusicScanner.RefreshLibraryCaches, and PlaylistRepository.UpdateSmartPlaylist/RemovePlaylist.
public static class CacheKeys
{
    // Whole-library lists - the only genuinely expensive cold recompute. Kept warm at all times:
    // mutated in place on a play, re-populated wholesale at the end of a scan.
    public const string TracksAll = "tracks:all";
    public const string AlbumsAll = "albums:all";
    public const string ArtistsAll = "artists:all";

    public const string RecentlyAdded = "tracks:recently-added";

    // Collection totals only. The listening half of the stats page is not cached at all - it is
    // per-user and per-time-range, so it would need an expiry this cache deliberately does not have.
    public const string LibraryStats = "stats:library";

    public const string TrackDetailsPrefix = "tracks:details:";
    public const string AlbumDetailsPrefix = "albums:details:";
    public const string ArtistDetailsPrefix = "artists:details:";
    public const string TrackDurationPrefix = "tracks:duration:";
    public const string TrackTrimPrefix = "tracks:trim:";
    public const string SortedViewPrefix = "tracks:sorted:";
    public const string RecentlyPlayedPrefix = "tracks:recently-played:";

    // Resolved track array for a single smart playlist. See the class-level comment above for the
    // invalidation invariant this depends on.
    public const string SmartPlaylistTracksPrefix = "playlists:smart:tracks:";

    public static string TrackDetails(uint id)
    {
        return $"{TrackDetailsPrefix}{id}";
    }

    public static string Album(uint id)
    {
        return $"{AlbumDetailsPrefix}{id}";
    }

    public static string Artist(uint id)
    {
        return $"{ArtistDetailsPrefix}{id}";
    }

    public static string TrackDuration(uint id)
    {
        return $"{TrackDurationPrefix}{id}";
    }

    public static string TrackTrim(uint id)
    {
        return $"{TrackTrimPrefix}{id}";
    }

    public static string RecentlyPlayed(int take)
    {
        return $"{RecentlyPlayedPrefix}{take}";
    }

    public static string SmartPlaylistTracks(uint id)
    {
        return $"{SmartPlaylistTracksPrefix}{id}";
    }

    // Per-sort cached array of the whole library, sharing TrackDto instances with TracksAll.
    public static string SortedView(TrackSortField sort, bool descending)
    {
        return $"{SortedViewPrefix}{sort}:{descending}";
    }
}
