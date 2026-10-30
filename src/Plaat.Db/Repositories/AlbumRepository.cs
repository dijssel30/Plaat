using Microsoft.EntityFrameworkCore;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for albums, including their tracks and cover art.
/// </summary>
public interface IAlbumRepository
{
    /// <summary>
    /// Gets full details (including tracks) for a single album. Returns null if not found.
    /// </summary>
    Task<AlbumDetailsDto?> GetAlbum(uint id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every album in the library.
    /// </summary>
    Task<IReadOnlyCollection<AlbumOnlyDto>> GetAlbums(CancellationToken ct);

    /// <summary>
    /// Recomputes and re-populates the cached whole-library album list. Called by the scanner so a
    /// scan refreshes the list without ever leaving it empty for the next visitor.
    /// </summary>
    Task RefreshListCache(CancellationToken ct);

    /// <summary>
    /// Gets the cover art for an album. Returns null if the album has none.
    /// </summary>
    Task<CoverArtDto?> GetCoverArt(uint id, CancellationToken ct);

    /// <summary>
    /// Sets (replacing any existing) the cover art for an album. Returns false if the album does not exist.
    /// </summary>
    Task<bool> SetCoverArt(uint id, string path, string hash, string mimeType, CancellationToken ct);

    /// <summary>
    /// Gets whether the user has marked an album as a favorite.
    /// </summary>
    Task<bool> IsAlbumFavorite(uint albumId, int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or removes an album from the user's favorites.
    /// </summary>
    Task SetAlbumFavorite(uint albumId, int userId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every album the user has marked as a favorite.
    /// </summary>
    Task<IReadOnlyCollection<AlbumOnlyDto>> GetFavoriteAlbums(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Add a play for each track in the album.
    /// </summary>
    Task CountAllTracksAsPlayed(uint albumId, int userId, CancellationToken cancellationToken);
}

internal sealed class AlbumRepository(
    IDbContextFactory<PlaatContext> contextFactory,
    ILibraryCache cache) : IAlbumRepository
{
    public async Task<AlbumDetailsDto?> GetAlbum(uint id, CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.Album(id),
            ct => GetAlbumFactory(id, ct),
            cancellationToken);
    }

    private async ValueTask<AlbumDetailsDto?> GetAlbumFactory(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var album = await context.Albums
            .AsSplitQuery()
            .Select(a => new
            {
                a.Id,
                a.Title,
                Artists = a.AlbumArtists.Select(aa => new ArtistOnlyDto { Id = aa.Id, Name = aa.Name }).ToArray(),
                Tracks = a.Tracks.Select(t => new
                {
                    t.BitRate,
                    Dto = new TrackDto
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
                    }
                }).ToArray()
            })
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (album is null)
        {
            return null;
        }

        // The same album can be stored twice in the library, which leaves the album with
        // duplicate track rows. Collapse tracks that match exactly on track number, title,
        // artist(s) and album, keeping the one with the highest play count and, when play
        // counts are equal, the one with the highest bitrate (quality).
        var dedupedTracks = album.Tracks
            .GroupBy(t => (
                t.Dto.TrackNumber,
                t.Dto.Title,
                Artists: string.Join('|', t.Dto.Artists.Select(artist => artist.Name)),
                t.Dto.Album))
            .Select(group => group
                .OrderByDescending(t => t.Dto.PlayCount)
                .ThenByDescending(t => t.BitRate)
                .First())
            .OrderBy(t => t.Dto.DiscNumber)
            .ThenBy(t => t.Dto.TrackNumber)
            .Select(t => t.Dto)
            .ToArray();

        return new AlbumDetailsDto
        {
            Id = album.Id,
            Title = album.Title,
            Artists = album.Artists,
            Tracks = dedupedTracks
        };
    }

    public async Task<IReadOnlyCollection<AlbumOnlyDto>> GetAlbums(CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(CacheKeys.AlbumsAll, GetAlbumsFactory, ct);
    }

    public async Task RefreshListCache(CancellationToken ct)
    {
        cache.Set(CacheKeys.AlbumsAll, await GetAlbumsFactory(ct));
    }

    public async Task<CoverArtDto?> GetCoverArt(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var album = await context.Albums
            .Include(a => a.CoverArt)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        var coverArt = album?.CoverArt;

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

    public async Task<bool> SetCoverArt(uint id, string path, string hash, string mimeType, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var album = await context.Albums.FirstOrDefaultAsync(a => a.Id == id, ct);

        if (album is null)
        {
            return false;
        }

        // Always attach a fresh CoverArt row rather than mutating the existing one - a single
        // CoverArt can be shared by several albums (dedup on scan), so editing in place would
        // change the cover for every album pointing at it.
        album.CoverArt = new CoverArt
        {
            Path = path,
            Hash = hash,
            MimeType = mimeType
        };

        await context.SaveChangesAsync(ct);

        // Cover art is not part of any cached list DTO, so only this album's cached detail
        // (which its tracks' bitrate/dedup view depends on) needs refreshing.
        cache.Remove(CacheKeys.Album(id));

        return true;
    }

    private async ValueTask<AlbumOnlyDto[]> GetAlbumsFactory(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var albums = await context.Albums
            .Select(a => new AlbumOnlyDto
            {
                Id = a.Id,
                Title = a.Title,
                Artists = a.AlbumArtists.Select(aa => new ArtistOnlyDto { Id = aa.Id, Name = aa.Name }).ToArray()
            })
            .ToArrayAsync(ct);
        return albums;
    }

    public async Task<bool> IsAlbumFavorite(uint albumId, int userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<FavoriteAlbum>()
            .AnyAsync(fa => fa.AlbumId == albumId && fa.UserId == userId, cancellationToken);
    }

    public async Task SetAlbumFavorite(uint albumId, int userId, bool isFavorite, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (isFavorite)
        {
            var alreadyFavorite = await context.Set<FavoriteAlbum>()
                .AnyAsync(fa => fa.AlbumId == albumId && fa.UserId == userId, cancellationToken);

            if (!alreadyFavorite)
            {
                context.Add(new FavoriteAlbum { AlbumId = albumId, UserId = userId, AddedDate = DateTime.UtcNow });
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            await context.Set<FavoriteAlbum>()
                .Where(fa => fa.AlbumId == albumId && fa.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<AlbumOnlyDto>> GetFavoriteAlbums(int userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<FavoriteAlbum>()
            .Where(fa => fa.UserId == userId)
            .Select(fa => fa.Album)
            .Select(a => new AlbumOnlyDto
            {
                Id = a.Id,
                Title = a.Title,
                Artists = a.AlbumArtists.Select(aa => new ArtistOnlyDto { Id = aa.Id, Name = aa.Name }).ToArray()
            })
            .ToArrayAsync(cancellationToken);
    }

    public async Task CountAllTracksAsPlayed(
        uint albumId,
        int userId,
        CancellationToken cancellationToken)
    {
        var time = DateTime.UtcNow;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var tracks = await context.Tracks
            .Include(t => t.Plays)
            .Where(a => a.AlbumId == albumId)
            .ToArrayAsync(cancellationToken);

        foreach (var track in tracks)
        {
            track.PlayCount++;
            track.Plays.Add(new Play { UserId = userId, DateTime = time });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
