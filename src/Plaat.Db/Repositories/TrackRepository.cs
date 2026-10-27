using Microsoft.EntityFrameworkCore;
using Plaat.Contract;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

public sealed class TrackPath
{
    public uint Id { get; init; }
    public required string Path { get; init; }
    public required string MimeType { get; init; }
}

/// <summary>
/// A track's admin-set playback trim points.
/// </summary>
public sealed record TrackTrimPoints(TimeSpan? TrimStart, TimeSpan? TrimEnd);

/// <summary>
/// Data access for tracks: listing, details, cover art, playback location and play-count tracking.
/// </summary>
public interface ITrackRepository
{
    /// <summary>
    /// Increments a track's play count and records a play history entry for it.
    /// </summary>
    Task IncreasePlayCount(uint id, int? userId, CancellationToken ct);

    /// <summary>
    /// Gets the on-disk path and MIME type to stream for a track, preferring its default,
    /// supported-format location. Returns null if the track has no location.
    /// </summary>
    Task<TrackPath?> GetPath(uint id, CancellationToken ct);

    /// <summary>
    /// Gets a summary of every track in the library.
    /// </summary>
    Task<IReadOnlyCollection<TrackDto>> GetAll(CancellationToken ct);

    /// <summary>
    /// Recomputes and re-populates the cached whole-library track list (and drops the derived
    /// per-sort views). Called by the scanner so a scan refreshes the list without ever leaving
    /// it empty for the next visitor.
    /// </summary>
    Task RefreshListCache(CancellationToken ct);

    /// <summary>
    /// Gets one filtered/sorted page of tracks plus the total match count, so a large library can
    /// be browsed without transferring it whole.
    /// </summary>
    Task<PagedResult<TrackDto>> GetTracksPage(TrackQuery query, CancellationToken ct);

    /// <summary>
    /// Gets the ordered IDs of every track matching a query. Used to build a play queue from a
    /// paged listing without downloading every track DTO.
    /// </summary>
    Task<IReadOnlyList<uint>> GetTrackIds(TrackQuery query, CancellationToken ct);

    /// <summary>
    /// Resolves a set of track IDs to their summaries, preserving the given order. Used to rebuild
    /// a persisted play queue on startup.
    /// </summary>
    Task<IReadOnlyList<TrackDto>> GetByIds(IReadOnlyCollection<uint> ids, CancellationToken ct);

    /// <summary>
    /// Gets the most recently played distinct tracks, most-recent first.
    /// </summary>
    Task<IReadOnlyList<TrackDto>> GetRecentlyPlayed(int take, CancellationToken ct);

    /// <summary>
    /// Gets standalone tracks and albums added after the given date, grouped separately.
    /// </summary>
    Task<RecentlyAddedDto> GetRecentlyAdded(DateTime limit, CancellationToken ct);

    /// <summary>
    /// Gets the cover art of the album a track belongs to. Returns null if there is none.
    /// </summary>
    Task<CoverArtDto?> GetCoverArt(uint id, CancellationToken ct);

    /// <summary>
    /// Gets full details for a single track, including its file metadata, locations and play history.
    /// Returns null if not found.
    /// </summary>
    Task<TrackDetailsDto?> GetTrackDetails(uint id, CancellationToken ct);

    /// <summary>
    /// Marks one of a track's locations as the default playback location.
    /// Returns false if locationId does not belong to trackId (nothing is changed in that case).
    /// </summary>
    Task<bool> SetDefaultLocation(uint trackId, uint locationId, CancellationToken ct);

    /// <summary>
    /// Gets all tracks flagged as possibly corrupt.
    /// </summary>
    Task<IReadOnlyCollection<PossiblyCorruptTrackDto>> GetPossiblyCorrupt(CancellationToken ct);

    /// <summary>
    /// Gets a single track's duration. Returns null if not found.
    /// </summary>
    Task<TimeSpan?> GetDuration(uint id, CancellationToken ct);

    /// <summary>
    /// Gets the IDs of every track that has not yet had its loudness measured. Used by
    /// <c>LoudnessAnalysisQueueService</c> - the first run naturally covers the whole existing
    /// library, later runs just catch up whatever's new.
    /// </summary>
    Task<IReadOnlyCollection<uint>> GetTrackIdsNeedingLoudnessAnalysis(CancellationToken ct);

    /// <summary>
    /// Persists a track's measured integrated loudness (LUFS). Does nothing if the track no
    /// longer exists.
    /// </summary>
    Task SetIntegratedLoudness(uint trackId, float? integratedLoudnessLufs, CancellationToken ct);

    /// <summary>
    /// Gets a single track's admin-set playback trim points. Returns null if the track does not
    /// exist; a non-null result with both fields null means "no trim set".
    /// </summary>
    Task<TrackTrimPoints?> GetTrimPoints(uint id, CancellationToken ct);

    /// <summary>
    /// Sets (or clears, by passing null) a track's admin-only playback trim points. Does nothing if
    /// the track no longer exists.
    /// </summary>
    Task SetTrimPoints(uint trackId, TimeSpan? trimStart, TimeSpan? trimEnd, CancellationToken ct);
}

internal sealed class TrackRepository(
    IDbContextFactory<PlaatContext> contextFactory,
    ILibraryCache cache) : ITrackRepository
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<IReadOnlyCollection<TrackDto>> GetAll(CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(CacheKeys.TracksAll, TracksFactory, ct);
    }

    public async Task RefreshListCache(CancellationToken ct)
    {
        cache.Set(CacheKeys.TracksAll, await TracksFactory(ct));
        cache.RemoveByPrefix(CacheKeys.SortedViewPrefix);
    }

    // Cached "whole library, already sorted one way" arrays, keyed by sort, so paging while
    // scrolling /tracks costs a Skip/Take over an array rather than an O(n log n) sort on every
    // Virtualize fetch. They share TrackDto instances with the TracksAll entry, so an in-place
    // play-count bump there shows up here too; only the Plays-ordered views need dropping when a
    // play reorders them.
    private ValueTask<TrackDto[]> GetSortedAll(TrackSortField sort, bool descending, CancellationToken ct)
    {
        return cache.GetOrCreateAsync(
            CacheKeys.SortedView(sort, descending),
            async token => Sort(await GetAll(token), sort, descending).ToArray(),
            ct);
    }

    private static IEnumerable<TrackDto> Sort(IEnumerable<TrackDto> src, TrackSortField field, bool descending)
    {
        return (field, descending) switch
        {
            (TrackSortField.Title, false) => src.OrderBy(t => t.Title, TextComparer),
            (TrackSortField.Title, true) => src.OrderByDescending(t => t.Title, TextComparer),
            (TrackSortField.Artist, false) => src.OrderBy(t => t.AllArtists, TextComparer),
            (TrackSortField.Artist, true) => src.OrderByDescending(t => t.AllArtists, TextComparer),
            (TrackSortField.Album, false) => src.OrderBy(t => t.Album, TextComparer),
            (TrackSortField.Album, true) => src.OrderByDescending(t => t.Album, TextComparer),
            (TrackSortField.Year, false) => src.OrderBy(t => t.Year),
            (TrackSortField.Year, true) => src.OrderByDescending(t => t.Year),
            (TrackSortField.Plays, false) => src.OrderBy(t => t.PlayCount),
            (TrackSortField.Plays, true) => src.OrderByDescending(t => t.PlayCount),
            _ => src
        };
    }

    // Same match rule the track table used client-side: title OR any artist name, matched
    // word-by-word (see SearchScoring) rather than as one contiguous substring. Matches are
    // ranked by relevance first (an exact title/artist match beats one that merely contains the
    // query), falling back to the caller's chosen column sort to break ties.
    private static TrackDto[] FilterAndSort(
        IReadOnlyCollection<TrackDto> tracks,
        TrackDto[] sortedView,
        TrackQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Search))
        {
            return sortedView;
        }

        var scores = tracks
            .Select(t => (t.Id, Score: SearchScoring.SearchScore(query.Search, t.Title, t.AllArtists)))
            .Where(x => x.Score is not null)
            .ToDictionary(x => x.Id, x => x.Score!.Value);

        return
        [
            .. Sort(tracks.Where(t => scores.ContainsKey(t.Id)), query.Sort, query.Descending)
                .OrderByDescending(t => scores[t.Id])
        ];
    }

    public async Task<PagedResult<TrackDto>> GetTracksPage(TrackQuery query, CancellationToken ct)
    {
        var all = await GetAll(ct);
        var sortedView = await GetSortedAll(query.Sort, query.Descending, ct);
        var matches = FilterAndSort(all, sortedView, query);

        IEnumerable<TrackDto> page = matches;

        if (query.Skip > 0)
        {
            page = page.Skip(query.Skip);
        }

        if (query.Take > 0)
        {
            page = page.Take(query.Take);
        }

        return new PagedResult<TrackDto>
        {
            Items = page.ToArray(),
            TotalCount = matches.Length
        };
    }

    public async Task<IReadOnlyList<uint>> GetTrackIds(TrackQuery query, CancellationToken ct)
    {
        var all = await GetAll(ct);
        var sortedView = await GetSortedAll(query.Sort, query.Descending, ct);

        return FilterAndSort(all, sortedView, query).Select(t => t.Id).ToArray();
    }

    public async Task<IReadOnlyList<TrackDto>> GetByIds(IReadOnlyCollection<uint> ids, CancellationToken ct)
    {
        var byId = (await GetAll(ct)).ToDictionary(t => t.Id);

        return ids.Select(byId.GetValueOrDefault).OfType<TrackDto>().ToArray();
    }

    public async Task<IReadOnlyList<TrackDto>> GetRecentlyPlayed(int take, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.RecentlyPlayed(take),
            token => RecentlyPlayedFactory(take, token),
            ct);
    }

    private async ValueTask<TrackDto[]> RecentlyPlayedFactory(int take, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Over-fetch (plays repeat the same track) then dedupe in order.
        var recentIds = await context.Plays
            .OrderByDescending(p => p.DateTime)
            .Select(p => p.TrackId)
            .Take(Math.Max(take, 1) * 5)
            .ToArrayAsync(ct);

        var orderedDistinct = recentIds.Distinct().Take(take);

        var byId = (await GetAll(ct)).ToDictionary(t => t.Id);

        return [.. orderedDistinct.Select(byId.GetValueOrDefault).OfType<TrackDto>()];
    }

    private async ValueTask<IReadOnlyCollection<TrackDto>> TracksFactory(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .AsSplitQuery()
            .Select(t => new TrackDto
            {
                Id = t.Id,
                PlayCount = t.PlayCount,
                Title = t.Title,
                Artists = t.Artists.Select(aa => new ArtistOnlyDto { Id = aa.Id, Name = aa.Name }).ToArray(),
                Album = t.Album.Title,
                AlbumId = t.AlbumId,
                Year = t.Year,
                RecordedDate = t.RecordedDate,
                TrackNumber = t.TrackNumber,
                DiscNumber = t.DiscNumber,
                Duration = t.Duration,
                IntegratedLoudnessLufs = t.IntegratedLoudnessLufs,
                TrimStart = t.TrimStart,
                TrimEnd = t.TrimEnd
            })
            .ToArrayAsync(ct);
    }

    public async Task<RecentlyAddedDto> GetRecentlyAdded(
        DateTime limit,
        CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.RecentlyAdded,
            ct => RecentlyAddedFactory(limit, ct),
            cancellationToken);
    }

    private async ValueTask<RecentlyAddedDto> RecentlyAddedFactory(
        DateTime limit,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var recentlyAddedTracks = await context.Tracks
            .Include(t => t.Album)
            .ThenInclude(a => a.Tracks)
            .Include(t => t.Album)
            .ThenInclude(a => a.AlbumArtists)
            .Include(t => t.Artists)
            .AsSplitQuery()
            .Where(t => t.AddedDate > limit)
            .ToArrayAsync(ct);

        var albums = new List<AlbumOnlyDto>();
        var tracks = new List<TrackDto>();

        foreach (var track in recentlyAddedTracks)
        {
            if (track.Album.Tracks.Count > 1) // track belongs to album, add it to the album list
            {
                if (albums.All(a => a.Id != track.AlbumId))
                {
                    albums.Add(new AlbumOnlyDto
                    {
                        Id = track.AlbumId,
                        Title = track.Album.Title,
                        Artists = track.Album.AlbumArtists
                            .Select(albumArtist => new ArtistOnlyDto { Id = albumArtist.Id, Name = albumArtist.Name })
                            .ToArray()
                            .AsReadOnly()
                    });
                }
            }
            else
            {
                tracks.Add(new TrackDto
                {
                    Id = track.Id,
                    Title = track.Title,
                    Artists = track.Artists.Select(a => new ArtistOnlyDto
                    {
                        Id = a.Id,
                        Name = a.Name
                    }).ToArray().AsReadOnly(),
                    Album = track.Album.Title,
                    AlbumId = track.AlbumId,
                    PlayCount = track.PlayCount,
                    TrackNumber = track.TrackNumber,
                    DiscNumber = track.DiscNumber,
                    Duration = track.Duration,
                    Year = track.Year,
                    RecordedDate = track.RecordedDate,
                    IntegratedLoudnessLufs = track.IntegratedLoudnessLufs,
                    TrimStart = track.TrimStart,
                    TrimEnd = track.TrimEnd
                });
            }
        }

        return new RecentlyAddedDto
        {
            Albums = albums.AsReadOnly(),
            Tracks = tracks.AsReadOnly()
        };
    }

    public async Task<TrackPath?> GetPath(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var track = await context.TrackLocations
            .Include(t => t.Track)
            .OrderByDescending(t => t.Default)
            .ThenByDescending(t => t.IsSupportedFormat)
            .Select(t => new TrackPath { Id = t.TrackId, Path = t.Path, MimeType = t.Track.MimeType })
            .Where(t => t.Id == id)
            .FirstOrDefaultAsync(ct);

        return track;
    }

    public async Task IncreasePlayCount(uint id, int? userId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var file = await context.Tracks
            .Include(t => t.Plays)
            .Include(t => t.Artists)
            .AsSplitQuery()
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (file is null)
        {
            return;
        }

        var playedAt = DateTime.UtcNow;

        file.PlayCount += 1;

        file.Plays.Add(new Play
        {
            DateTime = playedAt,
            UserId = userId
        });

        await context.SaveChangesAsync(ct);

        // A play count changed. Instead of evicting the big track/album/artist list caches - the
        // single largest recurring source of cold-cache stalls - bump the count on the cached DTOs
        // in place and patch stats. Nothing expensive is thrown away.
        var artistIds = file.Artists.Select(a => a.Id).ToArray();

        cache.Mutate<IReadOnlyCollection<TrackDto>>(CacheKeys.TracksAll, tracks => BumpPlayCount(tracks, id));

        // Only the Plays-ordered views are re-ordered by a play; other sorts are untouched.
        cache.Remove(CacheKeys.SortedView(TrackSortField.Plays, false));
        cache.Remove(CacheKeys.SortedView(TrackSortField.Plays, true));

        // "Recently played" is defined by play recency, so it genuinely changes on every play.
        cache.RemoveByPrefix(CacheKeys.RecentlyPlayedPrefix);

        // The track's own detail also carries play history - a single-row rebuild, just drop it.
        cache.Remove(CacheKeys.TrackDetails(id));

        cache.Mutate<AlbumDetailsDto>(CacheKeys.Album(file.AlbumId), album => BumpPlayCount(album.Tracks, id));

        foreach (var artistId in artistIds)
        {
            cache.Mutate<ArtistDetailsDto>(CacheKeys.Artist(artistId), artist => BumpPlayCount(artist.Tracks, id));
        }

        cache.Mutate<RecentlyAddedDto>(CacheKeys.RecentlyAdded, recent => BumpPlayCount(recent.Tracks, id));

        // A play can change whether a track matches a PlayCount or NotPlayedInDays smart playlist
        // rule for any user - no per-playlist index of which rule types are in use exists, so drop
        // every smart playlist's cached tracks rather than trying to pinpoint which ones are affected.
        cache.RemoveByPrefix(CacheKeys.SmartPlaylistTracksPrefix);

        // Nothing to do for the stats page: its listening half is computed per request, and its
        // library totals (CacheKeys.LibraryStats) do not move when a track is played.
    }

    private static void BumpPlayCount(IEnumerable<TrackDto> tracks, uint trackId)
    {
        foreach (var track in tracks)
        {
            if (track.Id == trackId)
            {
                track.PlayCount++;
            }
        }
    }

    private static void SetTrim(IEnumerable<TrackDto> tracks, uint trackId, TimeSpan? trimStart, TimeSpan? trimEnd)
    {
        foreach (var track in tracks)
        {
            if (track.Id == trackId)
            {
                track.TrimStart = trimStart;
                track.TrimEnd = trimEnd;
            }
        }
    }

    public async Task<CoverArtDto?> GetCoverArt(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var track = await context.Tracks
            .Include(t => t.Album)
            .ThenInclude(a => a.CoverArt)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        var coverArt = track?.Album.CoverArt;

        if (coverArt is null)
        {
            return null;
        }

        return new CoverArtDto
        {
            Hash = coverArt.Hash,
            Path = coverArt.Path,
            MimeType = coverArt.MimeType
        };
    }

    public async Task<TrackDetailsDto?> GetTrackDetails(uint id, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.TrackDetails(id),
            token => GetTrackDetailsFactory(id, token),
            ct);
    }

    private async ValueTask<TrackDetailsDto?> GetTrackDetailsFactory(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .AsSplitQuery()
            .Where(t => t.Id == id)
            .Select(t => new TrackDetailsDto
            {
                Id = t.Id,
                PlayCount = t.PlayCount,
                Title = t.Title,
                Artists = t.Artists.Select(aa => new ArtistOnlyDto { Id = aa.Id, Name = aa.Name }).ToArray(),
                Album = t.Album.Title,
                AlbumId = t.AlbumId,
                Year = t.Year,
                RecordedDate = t.RecordedDate,
                TrackNumber = t.TrackNumber,
                DiscNumber = t.DiscNumber,
                Duration = t.Duration,
                IntegratedLoudnessLufs = t.IntegratedLoudnessLufs,
                TrimStart = t.TrimStart,
                TrimEnd = t.TrimEnd,
                AddedDate = t.AddedDate,
                PossiblyCorrupt = t.PossiblyCorrupt,
                BitRate = t.BitRate,
                SampleRate = t.SampleRate,
                BitsPerSample = t.BitsPerSample,
                FileHash = t.FileHash,
                FileSizeBytes = t.FileSizeBytes,
                OriginalContentHash = t.OriginalContentHash,
                MimeType = t.MimeType,
                Format = t.Format,
                Lyrics = t.Lyrics,
                Locations = t.TrackLocations.Select(l => new TrackLocationDto
                {
                    Id = l.Id,
                    Path = l.Path,
                    IsSupportedFormat = l.IsSupportedFormat,
                    IsDefault = l.Default
                }).ToArray(),
                PlayHistory = t.Plays.Select(p => p.DateTime).ToArray()
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> SetDefaultLocation(uint trackId, uint locationId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var locations = await context.TrackLocations
            .Where(l => l.TrackId == trackId)
            .ToArrayAsync(ct);

        if (locations.All(l => l.Id != locationId))
        {
            return false;
        }

        foreach (var location in locations)
        {
            location.Default = location.Id == locationId;
        }

        await context.SaveChangesAsync(ct);
        cache.Remove(CacheKeys.TrackDetails(trackId));

        return true;
    }

    public async Task<IReadOnlyCollection<PossiblyCorruptTrackDto>> GetPossiblyCorrupt(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .Where(t => t.PossiblyCorrupt)
            .AsSplitQuery()
            .Select(t => new PossiblyCorruptTrackDto(
                t.Id,
                t.Title,
                string.Join(", ", t.Artists.Select(a => a.Name)),
                t.Album.Title,
                t.AlbumId,
                t.TrackLocations.Select(l => l.Path).ToArray()))
            .ToArrayAsync(ct);
    }

    public Task<TimeSpan?> GetDuration(uint id, CancellationToken ct)
    {
        return cache.GetOrCreateAsync(
            CacheKeys.TrackDuration(id),
            token => GetDurationFactory(id, token),
            ct).AsTask();
    }

    private async ValueTask<TimeSpan?> GetDurationFactory(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .Where(t => t.Id == id)
            .Select(t => (TimeSpan?)t.Duration)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyCollection<uint>> GetTrackIdsNeedingLoudnessAnalysis(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .Where(t => t.IntegratedLoudnessLufs == null)
            .Select(t => t.Id)
            .ToArrayAsync(ct);
    }

    public async Task SetIntegratedLoudness(uint trackId, float? integratedLoudnessLufs, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var track = await context.Tracks.FirstOrDefaultAsync(t => t.Id == trackId, ct);

        if (track is null)
        {
            return;
        }

        track.IntegratedLoudnessLufs = integratedLoudnessLufs;

        await context.SaveChangesAsync(ct);

        cache.Remove(CacheKeys.TrackDetails(trackId));
    }

    public Task<TrackTrimPoints?> GetTrimPoints(uint id, CancellationToken ct)
    {
        return cache.GetOrCreateAsync(
            CacheKeys.TrackTrim(id),
            token => GetTrimPointsFactory(id, token),
            ct).AsTask();
    }

    private async ValueTask<TrackTrimPoints?> GetTrimPointsFactory(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.Tracks
            .Where(t => t.Id == id)
            .Select(t => new TrackTrimPoints(t.TrimStart, t.TrimEnd))
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetTrimPoints(uint trackId, TimeSpan? trimStart, TimeSpan? trimEnd, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var track = await context.Tracks
            .Include(t => t.Artists)
            .FirstOrDefaultAsync(t => t.Id == trackId, ct);

        if (track is null)
        {
            return;
        }

        track.TrimStart = trimStart;
        track.TrimEnd = trimEnd;

        await context.SaveChangesAsync(ct);

        var artistIds = track.Artists.Select(a => a.Id).ToArray();

        cache.Mutate<IReadOnlyCollection<TrackDto>>(CacheKeys.TracksAll, tracks => SetTrim(tracks, trackId, trimStart, trimEnd));
        cache.Mutate<AlbumDetailsDto>(CacheKeys.Album(track.AlbumId), album => SetTrim(album.Tracks, trackId, trimStart, trimEnd));

        foreach (var artistId in artistIds)
        {
            cache.Mutate<ArtistDetailsDto>(CacheKeys.Artist(artistId), artist => SetTrim(artist.Tracks, trackId, trimStart, trimEnd));
        }

        cache.Mutate<RecentlyAddedDto>(CacheKeys.RecentlyAdded, recent => SetTrim(recent.Tracks, trackId, trimStart, trimEnd));
        cache.RemoveByPrefix(CacheKeys.RecentlyPlayedPrefix);

        cache.Remove(CacheKeys.TrackDetails(trackId));
        cache.Remove(CacheKeys.TrackTrim(trackId));
    }
}
