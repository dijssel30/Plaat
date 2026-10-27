using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Db.Expressions;
using Plaat.Db.Models;
using PlayCountPlaylistRuleType = Plaat.Db.Models.PlayCountPlaylistRuleType;
using StarsPlaylistRuleType = Plaat.Db.Models.StarsPlaylistRuleType;
using TextMatchType = Plaat.Db.Models.TextMatchType;
using SmartPlaylistGroupCombinator = Plaat.Db.Models.SmartPlaylistGroupCombinator;
using SmartPlaylistLimitOrderBy = Plaat.Db.Models.SmartPlaylistLimitOrderBy;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for regular and smart playlists: creation, track membership/ordering, pinning and deletion.
/// </summary>
public interface IPlaylistRepository
{
    /// <summary>
    /// Creates a new regular playlist for the user with the given tracks. Throws if a playlist
    /// with the same name (case-insensitive) already exists for that user.
    /// </summary>
    Task CreatePlaylist(
        string playlistName,
        int userId,
        IReadOnlyCollection<uint> tracks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new smart playlist for the user with the given nested rule-group tree and optional
    /// "limit to top N" setting. Throws if a playlist with the same name (case-insensitive) already
    /// exists for that user. Populates the cached track count immediately.
    /// </summary>
    Task CreateSmartPlaylist(
        string playlistName,
        int userId,
        SmartPlaylistRuleGroupDto rootGroup,
        int? limit,
        SmartPlaylistLimitOrderByDto? limitOrderBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a smart playlist's current rule tree and limit settings for editing. Returns null if no
    /// such playlist exists for the user.
    /// </summary>
    Task<SmartPlaylistRulesDto?> GetSmartPlaylistRules(
        uint id,
        int userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing smart playlist's entire rule tree and limit settings, and recalculates
    /// its cached track count. Returns false if no such playlist exists for the user.
    /// </summary>
    Task<bool> UpdateSmartPlaylist(
        uint id,
        int userId,
        SmartPlaylistRuleGroupDto rootGroup,
        int? limit,
        SmartPlaylistLimitOrderByDto? limitOrderBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends tracks not already in the playlist to its end, preserving order.
    /// </summary>
    Task AddToPlaylist(
        uint id,
        int userId,
        IReadOnlyCollection<uint> tracks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a single track from a playlist.
    /// </summary>
    Task RemoveFromPlaylist(
        uint id,
        int userId,
        uint trackId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reorders a playlist's tracks to match the given order.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if <paramref name="orderedTrackIds"/> doesn't contain exactly the
    /// same set of track ids currently in the playlist (no duplicates, none missing, none extra);
    /// the caller should reject the request in that case. <see langword="true"/> on success.
    /// </returns>
    Task<bool> ReorderPlaylist(
        uint id,
        int userId,
        IReadOnlyList<uint> orderedTrackIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Copies a regular or smart playlist belonging to the user under a new name: a regular
    /// playlist keeps the same tracks in the same order, a smart one gets its own copy of the rule
    /// tree and limit settings. The copy is never pinned, regardless of the original. Throws if a
    /// playlist with the new name (case-insensitive) already exists for that user.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if the source playlist doesn't exist for the user.
    /// </returns>
    Task<bool> DuplicatePlaylist(
        uint id,
        bool isSmart,
        int userId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a regular or smart playlist belonging to the user.
    /// </summary>
    Task RemovePlaylist(
        uint id,
        bool isSmart,
        int userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether a regular or smart playlist is pinned.
    /// </summary>
    Task SetPlaylistPinned(
        uint id,
        bool isSmart,
        int userId,
        bool isPinned,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every regular and smart playlist belonging to the user.
    /// </summary>
    Task<IReadOnlyCollection<PlaylistOnlyDto>> GetAllPlaylists(
        int userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets full details for a playlist by name (regular first, falling back to smart), including
    /// its resolved tracks. Returns null if no matching playlist exists for the user.
    /// </summary>
    Task<PlaylistDetailsDto?> GetPlaylist(
        string name,
        int userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recalculates and persists the cached track count for every smart playlist in the database
    /// (all users), skipping playlists whose count hasn't changed. Used by the periodic background
    /// recalculation job.
    /// </summary>
    Task RecalculateAllSmartPlaylistTrackCounts(CancellationToken cancellationToken);
}

internal sealed class PlaylistRepository(
    IDbContextFactory<PlaatContext> dbContextFactory,
    ILibraryCache cache) : IPlaylistRepository
{
    private const int MaxGroupDepth = SmartPlaylistRuleGroupLimits.MaxGroupDepth;

    private static async Task<bool> PlaylistNameExists(
        PlaatContext context,
        int userId,
        string playlistName,
        CancellationToken cancellationToken)
    {
        var lowerName = playlistName.ToLower();

        return await context.Playlists.AnyAsync(
                   pl => pl.UserId == userId && pl.Name.ToLower() == lowerName, cancellationToken)
               || await context.SmartPlaylists.AnyAsync(
                   pl => pl.UserId == userId && pl.Name.ToLower() == lowerName, cancellationToken);
    }

    /// <summary>
    /// Loads the given tracks in the order the IDs were given (a playlist's order is the caller's,
    /// e.g. an imported M3U file's), skipping IDs that don't exist.
    /// </summary>
    private static async Task<List<Track>> LoadTracksInOrder(
        PlaatContext context,
        IReadOnlyCollection<uint> trackIds,
        CancellationToken cancellationToken)
    {
        var tracksById = await context.Tracks
            .Where(t => trackIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return [.. trackIds.Where(tracksById.ContainsKey).Select(id => tracksById[id])];
    }

    public async Task CreatePlaylist(
        string playlistName,
        int userId,
        IReadOnlyCollection<uint> tracks,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await PlaylistNameExists(context, userId, playlistName, cancellationToken))
        {
            throw new DuplicateNameException("Playlist already exists");
        }

        tracks = tracks.Distinct().ToList();

        List<Track> dbTracks = [];

        if (tracks.Count > 0)
        {
            dbTracks = await LoadTracksInOrder(context, tracks, cancellationToken);
        }

        var playlist = new Playlist
        {
            Name = playlistName,
            UserId = userId,
            PlaylistTracks = [.. dbTracks.Select((t, index) => new PlaylistTrack { Track = t, Position = index })]
        };

        context.Add(playlist);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateSmartPlaylist(
        string playlistName,
        int userId,
        SmartPlaylistRuleGroupDto rootGroup,
        int? limit,
        SmartPlaylistLimitOrderByDto? limitOrderBy,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await PlaylistNameExists(context, userId, playlistName, cancellationToken))
        {
            throw new DuplicateNameException("Playlist already exists");
        }

        var playlist = new SmartPlaylist
        {
            Name = playlistName,
            UserId = userId,
            RootGroup = MapGroup(rootGroup, 0),
            Limit = limit,
            LimitOrderBy = limitOrderBy is null ? null : (SmartPlaylistLimitOrderBy)limitOrderBy
        };

        // One transaction so a failure while counting never leaves a persisted playlist behind.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        context.Add(playlist);

        await context.SaveChangesAsync(cancellationToken);

        playlist.TrackCount = await CountMatchingTracks(context, playlist, cancellationToken);
        playlist.TrackCountUpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SmartPlaylistRulesDto?> GetSmartPlaylistRules(
        uint id, int userId, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlist = await context.SmartPlaylists
            .Include(pl => pl.RootGroup).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .AsSplitQuery()
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (playlist is null)
        {
            return null;
        }

        return new SmartPlaylistRulesDto
        {
            RootGroup = MapGroupToDto(playlist.RootGroup),
            Limit = playlist.Limit,
            LimitOrderBy = playlist.LimitOrderBy is null
                ? null
                : (SmartPlaylistLimitOrderByDto)playlist.LimitOrderBy
        };
    }

    public async Task<bool> UpdateSmartPlaylist(
        uint id,
        int userId,
        SmartPlaylistRuleGroupDto rootGroup,
        int? limit,
        SmartPlaylistLimitOrderByDto? limitOrderBy,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlist = await context.SmartPlaylists
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (playlist is null)
        {
            return false;
        }

        // One transaction so a failure part-way never leaves an orphaned rule group behind.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var oldRootGroupId = playlist.RootGroupId;

        var newRootGroup = MapGroup(rootGroup, 0);
        context.Add(newRootGroup);
        await context.SaveChangesAsync(cancellationToken);

        // Repoint the playlist at the new group before deleting the old one - RootGroupId's FK is
        // configured Cascade (deleting a playlist deletes its group), so deleting the still-referenced
        // old group first would cascade-delete the playlist itself.
        playlist.RootGroupId = newRootGroup.Id;
        playlist.Limit = limit;
        playlist.LimitOrderBy = limitOrderBy is null ? null : (SmartPlaylistLimitOrderBy)limitOrderBy;

        await context.SaveChangesAsync(cancellationToken);

        await context.Set<SmartPlaylistRuleGroup>()
            .Where(g => g.Id == oldRootGroupId)
            .ExecuteDeleteAsync(cancellationToken);

        playlist.TrackCount = await CountMatchingTracks(context, playlist, cancellationToken);
        playlist.TrackCountUpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        cache.Remove(CacheKeys.SmartPlaylistTracks(id));

        return true;
    }

    public async Task AddToPlaylist(
        uint id,
        int userId,
        IReadOnlyCollection<uint> tracks,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlist = await context.Playlists
            .Include(p => p.PlaylistTracks)
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (playlist is null)
        {
            throw new Exception("Playlist does not exist");
        }

        var existingPlaylistTracks = playlist.PlaylistTracks.Select(t => t.TrackId).ToList();

        var tracksToAdd = tracks.Where(t => !existingPlaylistTracks.Contains(t)).Distinct().ToList();

        if (tracksToAdd.Count > 0)
        {
            var nextPosition = playlist.PlaylistTracks.Count == 0
                ? 0
                : playlist.PlaylistTracks.Max(pt => pt.Position) + 1;

            var dbTracks = await LoadTracksInOrder(context, tracksToAdd, cancellationToken);

            foreach (var dbTrack in dbTracks)
            {
                playlist.PlaylistTracks.Add(new PlaylistTrack { Track = dbTrack, Position = nextPosition++ });
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveFromPlaylist(
        uint id,
        int userId,
        uint trackId,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        await context.Set<PlaylistTrack>()
            .Where(pt => pt.TrackId == trackId
                         && pt.Playlist.Id == id
                         && pt.Playlist.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> ReorderPlaylist(
        uint id,
        int userId,
        IReadOnlyList<uint> orderedTrackIds,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlist = await context.Playlists
            .Include(p => p.PlaylistTracks)
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (playlist is null)
        {
            return false;
        }

        var tracksById = playlist.PlaylistTracks.ToDictionary(pt => pt.TrackId);

        var requestedTrackIds = orderedTrackIds.ToHashSet();

        if (requestedTrackIds.Count != orderedTrackIds.Count ||
            requestedTrackIds.Count != tracksById.Count ||
            !requestedTrackIds.SetEquals(tracksById.Keys))
        {
            return false;
        }

        for (var position = 0; position < orderedTrackIds.Count; position++)
        {
            tracksById[orderedTrackIds[position]].Position = position;
        }

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DuplicatePlaylist(
        uint id,
        bool isSmart,
        int userId,
        string newName,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await PlaylistNameExists(context, userId, newName, cancellationToken))
        {
            throw new DuplicateNameException("Playlist already exists");
        }

        return isSmart
            ? await DuplicateSmartPlaylist(context, id, userId, newName, cancellationToken)
            : await DuplicateRegularPlaylist(context, id, userId, newName, cancellationToken);
    }

    private static async Task<bool> DuplicateRegularPlaylist(
        PlaatContext context,
        uint id,
        int userId,
        string newName,
        CancellationToken cancellationToken)
    {
        var source = await context.Playlists
            .Include(pl => pl.PlaylistTracks)
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (source is null)
        {
            return false;
        }

        var copy = new Playlist
        {
            Name = newName,
            UserId = userId,
            PlaylistTracks =
            [
                .. source.PlaylistTracks
                    .OrderBy(pt => pt.Position)
                    .Select((pt, index) => new PlaylistTrack { TrackId = pt.TrackId, Position = index })
            ]
        };

        context.Add(copy);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static async Task<bool> DuplicateSmartPlaylist(
        PlaatContext context,
        uint id,
        int userId,
        string newName,
        CancellationToken cancellationToken)
    {
        var source = await context.SmartPlaylists
            .Include(pl => pl.RootGroup).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .AsSplitQuery()
            .FirstOrDefaultAsync(pl => pl.Id == id && pl.UserId == userId, cancellationToken);

        if (source is null)
        {
            return false;
        }

        var copy = new SmartPlaylist
        {
            Name = newName,
            UserId = userId,
            // Round-tripping through the DTO mapping is the cheapest way to get a detached deep copy
            // of the rule tree - reusing the source's own entities would move them to the copy.
            RootGroup = MapGroup(MapGroupToDto(source.RootGroup), 0),
            Limit = source.Limit,
            LimitOrderBy = source.LimitOrderBy,
            // The copy matches exactly the same tracks, so the source's cached count carries over
            // (and keeps its staleness - the periodic recalculation freshens both alike).
            TrackCount = source.TrackCount,
            TrackCountUpdatedAt = source.TrackCountUpdatedAt
        };

        context.Add(copy);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task RemovePlaylist(
        uint id,
        bool isSmart,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (isSmart)
        {
            var rootGroupId = await context.SmartPlaylists
                .Where(pl => pl.Id == id && pl.UserId == userId)
                .Select(pl => pl.RootGroupId)
                .FirstOrDefaultAsync(cancellationToken);

            if (rootGroupId != 0)
            {
                // Deleting the root group cascades to its rules and all nested child groups/rules
                // (SmartPlaylistRuleGroupConfiguration), and cascades up to delete the SmartPlaylist
                // row itself (SmartPlaylistConfiguration.RootGroup FK is configured as Cascade).
                await context.Set<SmartPlaylistRuleGroup>()
                    .Where(g => g.Id == rootGroupId)
                    .ExecuteDeleteAsync(cancellationToken);

                cache.Remove(CacheKeys.SmartPlaylistTracks(id));
            }
        }
        else
        {
            await context.Playlists
                .Where(pl => pl.Id == id && pl.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task SetPlaylistPinned(
        uint id,
        bool isSmart,
        int userId,
        bool isPinned,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (isSmart)
        {
            await context.SmartPlaylists
                .Where(pl => pl.Id == id && pl.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(pl => pl.IsPinned, isPinned), cancellationToken);
        }
        else
        {
            await context.Playlists
                .Where(pl => pl.Id == id && pl.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(pl => pl.IsPinned, isPinned), cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<PlaylistOnlyDto>> GetAllPlaylists(int userId, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalPlayLists = await context.Playlists
            .Where(pl => pl.UserId == userId)
            .Select(pl => new PlaylistOnlyDto { Id = pl.Id, PlaylistName = pl.Name, IsSmart = false, TrackCount = pl.PlaylistTracks.Count, IsPinned = pl.IsPinned })
            .ToArrayAsync(cancellationToken);

        var smartPlayLists = await context.SmartPlaylists
            .Where(pl => pl.UserId == userId)
            .Select(pl => new PlaylistOnlyDto { Id = pl.Id, PlaylistName = pl.Name, IsSmart = true, TrackCount = pl.TrackCount, IsPinned = pl.IsPinned })
            .ToArrayAsync(cancellationToken);

        return normalPlayLists
            .Concat(smartPlayLists)
            .ToArray();
    }

    public async Task<PlaylistDetailsDto?> GetPlaylist(string name, int userId, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playList = await context.Playlists
            .Where(pl => pl.UserId == userId && pl.Name.ToLower() == name.ToLower())
            .AsSplitQuery()
            .Select(pl => new PlaylistDetailsDto
            {
                IsSmart = false,
                Id = pl.Id,
                PlaylistName = pl.Name,
                TrackCount = pl.PlaylistTracks.Count,
                IsPinned = pl.IsPinned,
                Tracks = pl.PlaylistTracks
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.Track)
                    .Select(t => new TrackDto
                    {
                        Id = t.Id,
                        PlayCount = t.PlayCount,
                        Title = t.Title,
                        Artists = t.Artists.Select(artist => new ArtistOnlyDto
                        {
                            Id = artist.Id,
                            Name = artist.Name
                        }).ToArray(),
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
                    }).ToArray()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return playList ?? await GetSmartPlaylist(name, userId, cancellationToken);
    }

    private async Task<PlaylistDetailsDto?> GetSmartPlaylist(string name, int userId, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playListDb = await context.SmartPlaylists
            .Include(pl => pl.RootGroup).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .AsSplitQuery()
            .FirstOrDefaultAsync(pl => pl.UserId == userId && pl.Name.ToLower() == name.ToLower(), cancellationToken);

        if (playListDb is null)
        {
            return null;
        }

        // LoadMatchingTracks fully materializes into TrackDto[] before returning, so caching it here
        // is safe even though `context` is disposed at the end of this method.
        var playListTracks = await cache.GetOrCreateAsync(
            CacheKeys.SmartPlaylistTracks(playListDb.Id),
            async ct => await LoadMatchingTracks(context, playListDb, ct),
            cancellationToken);

        // Opportunistic write-through: freshens the cached count as soon as anyone actually opens
        // this playlist, rather than waiting for the next periodic recalculation tick.
        if (playListTracks.Length != playListDb.TrackCount)
        {
            playListDb.TrackCount = playListTracks.Length;
            playListDb.TrackCountUpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SmartPlaylistDetailsDto
        {
            IsSmart = true,
            Id = playListDb.Id,
            PlaylistName = playListDb.Name,
            Tracks = playListTracks,
            TrackCount = playListTracks.Length,
            IsPinned = playListDb.IsPinned
        };
    }

    public async Task RecalculateAllSmartPlaylistTrackCounts(CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlists = await context.SmartPlaylists
            .Include(pl => pl.RootGroup).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .Include(pl => pl.RootGroup).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.ChildGroups).ThenInclude(g => g.Rules)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        foreach (var playlist in playlists)
        {
            var count = await CountMatchingTracks(context, playlist, cancellationToken);

            if (count != playlist.TrackCount)
            {
                playlist.TrackCount = count;
                playlist.TrackCountUpdatedAt = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static readonly Expression<Func<Track, TrackDto>> TrackProjection = t => new TrackDto
    {
        Id = t.Id,
        PlayCount = t.PlayCount,
        Title = t.Title,
        Artists = t.Artists.Select(artist => new ArtistOnlyDto
        {
            Id = artist.Id,
            Name = artist.Name
        }).ToArray(),
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
    };

    /// <summary>
    /// Loads the tracks matching a smart playlist's rule tree as <see cref="TrackDto"/>, with
    /// <see cref="SmartPlaylist.Limit"/> applied. See <see cref="RequiresClientEvaluation"/> for why
    /// this sometimes evaluates client-side instead of pushing the query down to SQL.
    /// </summary>
    private static async Task<TrackDto[]> LoadMatchingTracks(
        PlaatContext context, SmartPlaylist playlist, CancellationToken cancellationToken)
    {
        var predicate = BuildGroupPredicate(playlist.RootGroup, playlist.UserId);

        if (RequiresClientEvaluation(playlist.RootGroup))
        {
            var matchingTracks = await LoadAllTracksForClientEvaluation(context, cancellationToken);
            var matched = ApplyLimit(matchingTracks.Where(predicate.Compile()), playlist);

            return [.. matched.Select(TrackProjection.Compile())];
        }

        var queryable = ApplyLimit(context.Tracks.Where(predicate), playlist);

        return await queryable.Select(TrackProjection).ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Counts the tracks matching a smart playlist's rule tree, with <see cref="SmartPlaylist.Limit"/>
    /// applied. See <see cref="RequiresClientEvaluation"/> for why this sometimes evaluates
    /// client-side instead of pushing the query down to SQL.
    /// </summary>
    private static async Task<int> CountMatchingTracks(
        PlaatContext context, SmartPlaylist playlist, CancellationToken cancellationToken)
    {
        var predicate = BuildGroupPredicate(playlist.RootGroup, playlist.UserId);

        if (RequiresClientEvaluation(playlist.RootGroup))
        {
            var matchingTracks = await LoadAllTracksForClientEvaluation(context, cancellationToken);

            return ApplyLimit(matchingTracks.Where(predicate.Compile()), playlist).Count();
        }

        var queryable = ApplyLimit(context.Tracks.Where(predicate), playlist);

        return await queryable.CountAsync(cancellationToken);
    }

    /// <summary>
    /// The UTC moment <paramref name="days"/> days before now, clamped to <see cref="DateTime.MinValue"/>
    /// so an absurdly large rule value cannot overflow <see cref="DateTime.AddDays"/>.
    /// </summary>
    private static DateTime DaysAgoUtc(uint days)
    {
        var now = DateTime.UtcNow;
        var maxDays = (now - DateTime.MinValue).TotalDays;

        return days >= maxDays ? DateTime.MinValue : now.AddDays(-(double)days);
    }

    private static Task<List<Track>> LoadAllTracksForClientEvaluation(
        PlaatContext context, CancellationToken cancellationToken)
    {
        return context.Tracks
            .Include(t => t.Artists)
            .Include(t => t.Album)
            .Include(t => t.Genres)
            .Include(t => t.Plays)
            .Include(t => t.Ratings)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Track> ApplyLimit(IQueryable<Track> queryable, SmartPlaylist playlist)
    {
        if (playlist.Limit is not { } limit)
        {
            return queryable;
        }

        return playlist.LimitOrderBy switch
        {
            SmartPlaylistLimitOrderBy.RecentlyAdded => queryable.OrderByDescending(t => t.AddedDate).Take(limit),
            _ => queryable
        };
    }

    private static IEnumerable<Track> ApplyLimit(IEnumerable<Track> tracks, SmartPlaylist playlist)
    {
        if (playlist.Limit is not { } limit)
        {
            return tracks;
        }

        return playlist.LimitOrderBy switch
        {
            SmartPlaylistLimitOrderBy.RecentlyAdded => tracks.OrderByDescending(t => t.AddedDate).Take(limit),
            _ => tracks
        };
    }

    /// <summary>
    /// Comparison operators on <see cref="Track.Duration"/> (a <see cref="TimeSpan"/>) aren't
    /// translatable by the SQLite provider (it's stored as ISO-8601 text, which doesn't sort
    /// numerically), so a rule tree containing a <see cref="DurationRangePlaylistRule"/> anywhere
    /// needs its predicate evaluated client-side against every track, instead of being pushed down
    /// to SQL. Fine for a personal, single-user library; would need reconsidering for a much larger
    /// one.
    /// </summary>
    private static bool RequiresClientEvaluation(SmartPlaylistRuleGroup group)
    {
        return group.Rules.Any(r => r is DurationRangePlaylistRule) || group.ChildGroups.Any(RequiresClientEvaluation);
    }

    private static SmartPlaylistRuleGroupDto MapGroupToDto(SmartPlaylistRuleGroup group)
    {
        return new SmartPlaylistRuleGroupDto
        {
            Combinator = (Contract.Models.SmartPlaylistGroupCombinator)group.Combinator,
            Rules = [.. group.Rules.Select(MapRuleToDto)],
            ChildGroups = [.. group.ChildGroups.Select(MapGroupToDto)]
        };
    }

    private static SmartPlaylistRuleDto MapRuleToDto(SmartPlaylistRule rule)
    {
        return rule switch
        {
            PlayCountPlaylistRule r => new PlayCountPlaylistRuleDto
            {
                PlayCount = r.PlayCount,
                PlayCountPlaylistRuleType = (Contract.Models.PlayCountPlaylistRuleType)r.PlayCountPlaylistRuleType
            },
            TitlePlaylistRule r => new TitlePlaylistRuleDto
            {
                Value = r.Value,
                TextMatchType = (Contract.Models.TextMatchType)r.TextMatchType
            },
            ArtistNamePlaylistRule r => new ArtistNamePlaylistRuleDto
            {
                Value = r.Value,
                TextMatchType = (Contract.Models.TextMatchType)r.TextMatchType
            },
            AlbumNamePlaylistRule r => new AlbumNamePlaylistRuleDto
            {
                Value = r.Value,
                TextMatchType = (Contract.Models.TextMatchType)r.TextMatchType
            },
            GenrePlaylistRule r => new GenrePlaylistRuleDto
            {
                Value = r.Value,
                TextMatchType = (Contract.Models.TextMatchType)r.TextMatchType
            },
            AddedDateRule r => new AddedDateRuleDto
            {
                Value = r.Value,
                RuleType = (DateComparisonType)r.DateRuleMatchType
            },
            AddedWithinDaysPlaylistRule r => new AddedWithinDaysRuleDto { Days = r.Days },
            NotPlayedInDaysPlaylistRule r => new NotPlayedInDaysRuleDto { Days = r.Days },
            DurationRangePlaylistRule r => new DurationRangeRuleDto
            {
                MinDuration = r.MinDuration,
                MaxDuration = r.MaxDuration
            },
            YearPlaylistRule r => new YearRuleDto
            {
                Year = r.Year,
                RuleType = (Contract.Models.PlayCountPlaylistRuleType)r.YearRuleMatchType
            },
            BitratePlaylistRule r => new BitrateRuleDto
            {
                Bitrate = r.Bitrate,
                RuleType = (Contract.Models.PlayCountPlaylistRuleType)r.BitrateRuleMatchType
            },
            StarsPlaylistRule r => new StarsRuleDto
            {
                Stars = r.Stars,
                RuleType = (StarsComparisonType)r.StarsRuleMatchType
            },
            _ => throw new InvalidOperationException($"Unhandled smart playlist rule type: {rule.GetType().Name}")
        };
    }

    private static SmartPlaylistRuleGroup MapGroup(SmartPlaylistRuleGroupDto dto, int depth)
    {
        return new SmartPlaylistRuleGroup
        {
            Combinator = (SmartPlaylistGroupCombinator)dto.Combinator,
            Rules = [.. dto.Rules.Select(MapRule)],
            ChildGroups = [.. dto.ChildGroups.Select(child =>
            {
                if (depth + 1 >= MaxGroupDepth)
                {
                    throw new InvalidOperationException(
                        "Smart playlist rule group nesting exceeds the maximum allowed depth");
                }

                return MapGroup(child, depth + 1);
            })]
        };
    }

    private static SmartPlaylistRule MapRule(SmartPlaylistRuleDto r)
    {
        return r switch
        {
            PlayCountPlaylistRuleDto playCountRule => new PlayCountPlaylistRule
            {
                PlayCount = playCountRule.PlayCount,
                PlayCountPlaylistRuleType = (PlayCountPlaylistRuleType)playCountRule.PlayCountPlaylistRuleType
            },
            TitlePlaylistRuleDto titleRule => new TitlePlaylistRule
            {
                Value = titleRule.Value,
                TextMatchType = (TextMatchType)titleRule.TextMatchType
            },
            ArtistNamePlaylistRuleDto artistRule => new ArtistNamePlaylistRule
            {
                Value = artistRule.Value,
                TextMatchType = (TextMatchType)artistRule.TextMatchType
            },
            AlbumNamePlaylistRuleDto albumRule => new AlbumNamePlaylistRule
            {
                Value = albumRule.Value,
                TextMatchType = (TextMatchType)albumRule.TextMatchType
            },
            GenrePlaylistRuleDto genreRule => new GenrePlaylistRule
            {
                Value = genreRule.Value,
                TextMatchType = (TextMatchType)genreRule.TextMatchType
            },
            AddedDateRuleDto dateTimeRule => new AddedDateRule
            {
                Value = dateTimeRule.Value,
                DateRuleMatchType = (PlayCountPlaylistRuleType)dateTimeRule.RuleType
            },
            AddedWithinDaysRuleDto addedWithinDaysRule => new AddedWithinDaysPlaylistRule
            {
                Days = addedWithinDaysRule.Days
            },
            NotPlayedInDaysRuleDto notPlayedInDaysRule => new NotPlayedInDaysPlaylistRule
            {
                Days = notPlayedInDaysRule.Days
            },
            DurationRangeRuleDto durationRangeRule => new DurationRangePlaylistRule
            {
                MinDuration = durationRangeRule.MinDuration,
                MaxDuration = durationRangeRule.MaxDuration
            },
            YearRuleDto yearRule => new YearPlaylistRule
            {
                Year = yearRule.Year,
                YearRuleMatchType = (PlayCountPlaylistRuleType)yearRule.RuleType
            },
            BitrateRuleDto bitrateRule => new BitratePlaylistRule
            {
                Bitrate = bitrateRule.Bitrate,
                BitrateRuleMatchType = (PlayCountPlaylistRuleType)bitrateRule.RuleType
            },
            StarsRuleDto starsRule => new StarsPlaylistRule
            {
                Stars = Math.Clamp(starsRule.Stars, TrackRating.MinStars, TrackRating.MaxStars),
                StarsRuleMatchType = (StarsPlaylistRuleType)starsRule.RuleType
            },
            _ => throw new Exception("Unable to map rule type")
        };
    }

    /// <summary>
    /// Builds the LINQ predicate for a rule group. <paramref name="userId"/> is the playlist owner:
    /// rules over per-user data (star ratings) resolve against that user, so the same rule tree
    /// yields different tracks for different owners.
    /// </summary>
    private static Expression<Func<Track, bool>> BuildGroupPredicate(SmartPlaylistRuleGroup group, int userId)
    {
        var predicates = group.Rules.Select(rule => BuildRulePredicate(rule, userId))
            .Concat(group.ChildGroups.Select(child => BuildGroupPredicate(child, userId)))
            .ToList();

        if (predicates.Count == 0)
        {
            return group.Combinator == SmartPlaylistGroupCombinator.And
                ? PredicateBuilderExtensions.True<Track>()
                : PredicateBuilderExtensions.False<Track>();
        }

        return predicates.Aggregate((left, right) => group.Combinator == SmartPlaylistGroupCombinator.And
            ? left.AndAlso(right)
            : left.OrElse(right));
    }

    internal static Expression<Func<Track, bool>> BuildRulePredicate(SmartPlaylistRule rule, int userId)
    {
        return rule switch
        {
            PlayCountPlaylistRule r => r.PlayCountPlaylistRuleType switch
            {
                PlayCountPlaylistRuleType.HigherOrEqual => t => t.PlayCount >= r.PlayCount,
                PlayCountPlaylistRuleType.LowerOrEqual => t => t.PlayCount <= r.PlayCount,
                PlayCountPlaylistRuleType.HigherThan => t => t.PlayCount > r.PlayCount,
                PlayCountPlaylistRuleType.LowerThan => t => t.PlayCount < r.PlayCount,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            TitlePlaylistRule r => r.TextMatchType switch
            {
                TextMatchType.Contains => t => t.Title.Contains(r.Value),
                TextMatchType.StartsWith => t => t.Title.StartsWith(r.Value),
                TextMatchType.EndsWith => t => t.Title.EndsWith(r.Value),
                TextMatchType.Equals => t => t.Title == r.Value,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            ArtistNamePlaylistRule r => r.TextMatchType switch
            {
                TextMatchType.Contains => t => t.Artists.Any(a => a.Name.Contains(r.Value)),
                TextMatchType.StartsWith => t => t.Artists.Any(a => a.Name.StartsWith(r.Value)),
                TextMatchType.EndsWith => t => t.Artists.Any(a => a.Name.EndsWith(r.Value)),
                TextMatchType.Equals => t => t.Artists.Any(a => a.Name == r.Value),
                _ => PredicateBuilderExtensions.True<Track>()
            },
            AlbumNamePlaylistRule r => r.TextMatchType switch
            {
                TextMatchType.Contains => t => t.Album.Title.Contains(r.Value),
                TextMatchType.StartsWith => t => t.Album.Title.StartsWith(r.Value),
                TextMatchType.EndsWith => t => t.Album.Title.EndsWith(r.Value),
                TextMatchType.Equals => t => t.Album.Title == r.Value,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            GenrePlaylistRule r => r.TextMatchType switch
            {
                TextMatchType.Contains => t => t.Genres.Any(g => g.Name.Contains(r.Value)),
                TextMatchType.StartsWith => t => t.Genres.Any(g => g.Name.StartsWith(r.Value)),
                TextMatchType.EndsWith => t => t.Genres.Any(g => g.Name.EndsWith(r.Value)),
                TextMatchType.Equals => t => t.Genres.Any(g => g.Name == r.Value),
                _ => PredicateBuilderExtensions.True<Track>()
            },
            // t.AddedDate is a plain DateTime column; comparing it directly against a DateTimeOffset
            // isn't translatable by the SQLite provider, so compare against the UTC DateTime instead.
            AddedDateRule r => r.DateRuleMatchType switch
            {
                PlayCountPlaylistRuleType.HigherOrEqual => t => t.AddedDate >= r.Value.UtcDateTime,
                PlayCountPlaylistRuleType.LowerOrEqual => t => t.AddedDate <= r.Value.UtcDateTime,
                PlayCountPlaylistRuleType.HigherThan => t => t.AddedDate > r.Value.UtcDateTime,
                PlayCountPlaylistRuleType.LowerThan => t => t.AddedDate < r.Value.UtcDateTime,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            AddedWithinDaysPlaylistRule r => t => t.AddedDate >= DaysAgoUtc(r.Days),
            NotPlayedInDaysPlaylistRule r => t =>
                !t.Plays.Any() || t.Plays.Max(p => p.DateTime) < DaysAgoUtc(r.Days),
            DurationRangePlaylistRule r => BuildDurationRangePredicate(r),
            YearPlaylistRule r => r.YearRuleMatchType switch
            {
                PlayCountPlaylistRuleType.HigherOrEqual => t => t.Year >= r.Year,
                PlayCountPlaylistRuleType.LowerOrEqual => t => t.Year <= r.Year,
                PlayCountPlaylistRuleType.HigherThan => t => t.Year > r.Year,
                PlayCountPlaylistRuleType.LowerThan => t => t.Year < r.Year,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            BitratePlaylistRule r => r.BitrateRuleMatchType switch
            {
                PlayCountPlaylistRuleType.HigherOrEqual => t => t.BitRate >= r.Bitrate,
                PlayCountPlaylistRuleType.LowerOrEqual => t => t.BitRate <= r.Bitrate,
                PlayCountPlaylistRuleType.HigherThan => t => t.BitRate > r.Bitrate,
                PlayCountPlaylistRuleType.LowerThan => t => t.BitRate < r.Bitrate,
                _ => PredicateBuilderExtensions.True<Track>()
            },
            StarsPlaylistRule r => BuildStarsPredicate(r, userId),
            _ => throw new InvalidOperationException($"Unhandled smart playlist rule type: {rule.GetType().Name}")
        };
    }

    /// <summary>
    /// A track the user never rated has no <see cref="TrackRating"/> row at all, but has to compare
    /// as <see cref="TrackRating.MinStars"/> rather than drop out of the result. Phrasing every
    /// operator as an Any/!Any over the rating rows gets that for free - and stays translatable to
    /// SQL - where a "rating ?? 0" projection would not. The two guards cover the operators where
    /// that flips the answer for unrated tracks: nothing is below 0 stars, and everything is at
    /// least 0.
    /// </summary>
    private static Expression<Func<Track, bool>> BuildStarsPredicate(StarsPlaylistRule rule, int userId)
    {
        var stars = rule.Stars;

        return rule.StarsRuleMatchType switch
        {
            StarsPlaylistRuleType.Equals when stars <= TrackRating.MinStars =>
                t => !t.Ratings.Any(r => r.UserId == userId && r.Stars > TrackRating.MinStars),
            StarsPlaylistRuleType.Equals =>
                t => t.Ratings.Any(r => r.UserId == userId && r.Stars == stars),
            StarsPlaylistRuleType.HigherThan =>
                t => t.Ratings.Any(r => r.UserId == userId && r.Stars > stars),
            StarsPlaylistRuleType.HigherOrEqual when stars <= TrackRating.MinStars =>
                PredicateBuilderExtensions.True<Track>(),
            StarsPlaylistRuleType.HigherOrEqual =>
                t => t.Ratings.Any(r => r.UserId == userId && r.Stars >= stars),
            StarsPlaylistRuleType.LowerThan when stars <= TrackRating.MinStars =>
                PredicateBuilderExtensions.False<Track>(),
            StarsPlaylistRuleType.LowerThan =>
                t => !t.Ratings.Any(r => r.UserId == userId && r.Stars >= stars),
            StarsPlaylistRuleType.LowerOrEqual =>
                t => !t.Ratings.Any(r => r.UserId == userId && r.Stars > stars),
            _ => PredicateBuilderExtensions.True<Track>()
        };
    }

    private static Expression<Func<Track, bool>> BuildDurationRangePredicate(DurationRangePlaylistRule r)
    {
        // Comparison operators directly on a TimeSpan column aren't translatable by the SQLite
        // provider (it's stored as ISO-8601 text, which doesn't sort numerically) - compare tick
        // counts instead.
        Expression<Func<Track, bool>>? predicate = null;

        if (r.MinDuration is { } min)
        {
            var minTicks = min.Ticks;
            predicate = t => t.Duration.Ticks >= minTicks;
        }

        if (r.MaxDuration is { } max)
        {
            var maxTicks = max.Ticks;
            Expression<Func<Track, bool>> maxPredicate = t => t.Duration.Ticks <= maxTicks;
            predicate = predicate is null ? maxPredicate : predicate.AndAlso(maxPredicate);
        }

        return predicate ?? PredicateBuilderExtensions.True<Track>();
    }
}
