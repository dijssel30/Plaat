using Microsoft.EntityFrameworkCore;
using Plaat.Contract;
using Plaat.Contract.Models;
using DbArtistDiscogsLookupStatus = Plaat.Db.Models.ArtistDiscogsLookupStatus;
using FavoriteArtist = Plaat.Db.Models.FavoriteArtist;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for artists, including their tracks/albums and Discogs enrichment state.
/// </summary>
public interface IArtistRepository
{
    /// <summary>
    /// Gets full details (including tracks and albums) for a single artist. Returns null if not found.
    /// </summary>
    Task<ArtistDetailsDto?> GetArtist(uint id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every artist in the library.
    /// </summary>
    Task<IReadOnlyCollection<ArtistOnlyDto>> GetArtists(CancellationToken ct);

    /// <summary>
    /// Recomputes and re-populates the cached whole-library artist list. Called by the scanner so a
    /// scan refreshes the list without ever leaving it empty for the next visitor.
    /// </summary>
    Task RefreshListCache(CancellationToken ct);

    /// <summary>
    /// Atomically claims an artist for Discogs enrichment by moving it from NotAttempted to
    /// InProgress. Returns false if it was already claimed or not eligible.
    /// </summary>
    Task<bool> TryClaimForEnrichment(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an artist's name by ID. Returns null if not found.
    /// </summary>
    Task<string?> GetArtistName(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the outcome of a Discogs enrichment attempt for an artist.
    /// </summary>
    Task SaveEnrichmentResult(
        uint artistId,
        int? discogsArtistId,
        string? summary,
        string? imagePath,
        DbArtistDiscogsLookupStatus status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resets an artist's Discogs lookup status back to NotAttempted so it can be retried.
    /// </summary>
    Task ResetToNotAttempted(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the stored Discogs image path for an artist. Returns null if none is set.
    /// </summary>
    Task<string?> GetDiscogsImagePath(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets whether the user has marked an artist as a favorite.
    /// </summary>
    Task<bool> IsArtistFavorite(uint artistId, int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or removes an artist from the user's favorites.
    /// </summary>
    Task SetArtistFavorite(uint artistId, int userId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every artist the user has marked as a favorite.
    /// </summary>
    Task<IReadOnlyCollection<ArtistOnlyDto>> GetFavoriteArtists(int userId, CancellationToken cancellationToken);
}

internal sealed class ArtistRepository(
    IDbContextFactory<PlaatContext> contextFactory,
    ILibraryCache cache) : IArtistRepository
{
    public async Task<ArtistDetailsDto?> GetArtist(uint id, CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.Artist(id),
            ct => ArtistFactory(id, ct),
            cancellationToken);
    }

    private async ValueTask<ArtistDetailsDto?> ArtistFactory(uint id, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var artist = await context.Artists
            .AsSplitQuery()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.DiscogsSummary,
                HasDiscogsImage = a.DiscogsImagePath != null,
                DiscogsLookupStatus = a.DiscogsLookupStatus.ToString(),
                Tracks = a.Tracks.Select(t => new TrackDto
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
                }).ToArray(),
                Albums = a.Albums
                    .Where(album => !album.Title.EndsWith(" Single") && album.Tracks.Count > 1)
                    .OrderByDescending(album => album.Tracks.First().Year)
                    .Select(album => new AlbumOnlyDto
                    {
                        Id = album.Id,
                        Title = album.Title,
                        Artists = album.AlbumArtists.Select(albumArtist => new ArtistOnlyDto { Id = albumArtist.Id, Name = albumArtist.Name }).ToArray()
                    }).ToArray()
            })
            .FirstOrDefaultAsync(ct);

        if (artist is null)
        {
            return null;
        }

        return new ArtistDetailsDto
        {
            Id = artist.Id,
            Name = artist.Name,
            DiscogsSummary = artist.DiscogsSummary,
            HasDiscogsImage = artist.HasDiscogsImage,
            DiscogsLookupStatus = Enum.Parse<ArtistDiscogsLookupStatus>(artist.DiscogsLookupStatus),
            Tracks = artist.Tracks,
            Albums = artist.Albums
        };
    }

    public async Task<IReadOnlyCollection<ArtistOnlyDto>> GetArtists(CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(CacheKeys.ArtistsAll, GetArtistsFactory, ct);
    }

    public async Task RefreshListCache(CancellationToken ct)
    {
        cache.Set(CacheKeys.ArtistsAll, await GetArtistsFactory(ct));
    }

    private async ValueTask<ArtistOnlyDto[]> GetArtistsFactory(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var artists = await context.Artists
            .Select(a => new ArtistOnlyDto { Id = a.Id, Name = a.Name })
            .ToArrayAsync(ct);

        return artists;
    }

    public async Task<bool> TryClaimForEnrichment(uint artistId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var updated = await context.Artists
            .Where(a => a.Id == artistId && a.DiscogsLookupStatus == DbArtistDiscogsLookupStatus.NotAttempted)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.DiscogsLookupStatus, DbArtistDiscogsLookupStatus.InProgress),
                cancellationToken);

        return updated > 0;
    }

    public async Task<string?> GetArtistName(uint artistId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Artists
            .Where(a => a.Id == artistId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveEnrichmentResult(
        uint artistId,
        int? discogsArtistId,
        string? summary,
        string? imagePath,
        DbArtistDiscogsLookupStatus status,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Artists
            .Where(a => a.Id == artistId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.DiscogsArtistId, discogsArtistId)
                    .SetProperty(a => a.DiscogsSummary, summary)
                    .SetProperty(a => a.DiscogsImagePath, imagePath)
                    .SetProperty(a => a.DiscogsLookupStatus, status),
                cancellationToken);

        cache.Remove(CacheKeys.Artist(artistId));
    }

    public async Task ResetToNotAttempted(uint artistId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Artists
            .Where(a => a.Id == artistId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.DiscogsLookupStatus, DbArtistDiscogsLookupStatus.NotAttempted),
                cancellationToken);

        cache.Remove(CacheKeys.Artist(artistId));
    }

    public async Task<string?> GetDiscogsImagePath(uint artistId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Artists
            .Where(a => a.Id == artistId)
            .Select(a => a.DiscogsImagePath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsArtistFavorite(uint artistId, int userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<FavoriteArtist>()
            .AnyAsync(fa => fa.ArtistId == artistId && fa.UserId == userId, cancellationToken);
    }

    public async Task SetArtistFavorite(uint artistId, int userId, bool isFavorite, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (isFavorite)
        {
            var alreadyFavorite = await context.Set<FavoriteArtist>()
                .AnyAsync(fa => fa.ArtistId == artistId && fa.UserId == userId, cancellationToken);

            if (!alreadyFavorite)
            {
                context.Add(new FavoriteArtist { ArtistId = artistId, UserId = userId, AddedDate = DateTime.UtcNow });
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            await context.Set<FavoriteArtist>()
                .Where(fa => fa.ArtistId == artistId && fa.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<ArtistOnlyDto>> GetFavoriteArtists(int userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<FavoriteArtist>()
            .Where(fa => fa.UserId == userId)
            .Select(fa => fa.Artist)
            .Select(a => new ArtistOnlyDto { Id = a.Id, Name = a.Name })
            .ToArrayAsync(cancellationToken);
    }
}
