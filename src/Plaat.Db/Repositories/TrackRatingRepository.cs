using Microsoft.EntityFrameworkCore;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for per-user star ratings. Deliberately separate from <see cref="ITrackRepository"/>:
/// that one serves every track DTO out of a process-wide cache shared by all users, which a per-user
/// value can never live in.
/// </summary>
public interface ITrackRatingRepository
{
    /// <summary>
    /// Gets the user's rating for a track. Returns <see cref="TrackRating.MinStars"/> when they
    /// never rated it.
    /// </summary>
    Task<int> GetRating(uint trackId, int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the user's rating for a track, clamped to
    /// <see cref="TrackRating.MinStars"/>..<see cref="TrackRating.MaxStars"/>. Setting it back to
    /// <see cref="TrackRating.MinStars"/> deletes the row instead of storing a zero.
    /// </summary>
    /// <returns><see langword="false"/> if no such track exists; nothing is stored in that case.</returns>
    Task<bool> SetRating(uint trackId, int userId, int stars, CancellationToken cancellationToken);
}

internal sealed class TrackRatingRepository(
    IDbContextFactory<PlaatContext> contextFactory,
    ILibraryCache cache) : ITrackRatingRepository
{
    public async Task<int> GetRating(uint trackId, int userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<TrackRating>()
            .Where(tr => tr.TrackId == trackId && tr.UserId == userId)
            .Select(tr => tr.Stars)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<uint, int>> GetRatings(
        IReadOnlyCollection<uint> trackIds,
        int userId,
        CancellationToken cancellationToken)
    {
        if (trackIds.Count == 0)
        {
            return new Dictionary<uint, int>();
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<TrackRating>()
            .Where(tr => tr.UserId == userId && trackIds.Contains(tr.TrackId))
            .ToDictionaryAsync(tr => tr.TrackId, tr => tr.Stars, cancellationToken);
    }

    public async Task<bool> SetRating(uint trackId, int userId, int stars, CancellationToken cancellationToken)
    {
        stars = Math.Clamp(stars, TrackRating.MinStars, TrackRating.MaxStars);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Tracks.AnyAsync(t => t.Id == trackId, cancellationToken))
        {
            return false;
        }

        if (stars == TrackRating.MinStars)
        {
            await context.Set<TrackRating>()
                .Where(tr => tr.TrackId == trackId && tr.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            // No per-playlist index of which rule types a playlist uses exists (by design), so a
            // rating change conservatively drops every smart playlist's cached tracks rather than
            // the rater's own - scoping this down would cost an extra query on every rating write
            // to save nothing in the common single-user case.
            cache.RemoveByPrefix(CacheKeys.SmartPlaylistTracksPrefix);

            return true;
        }

        var existing = await context.Set<TrackRating>()
            .FirstOrDefaultAsync(tr => tr.TrackId == trackId && tr.UserId == userId, cancellationToken);

        if (existing is null)
        {
            context.Add(new TrackRating
            {
                TrackId = trackId,
                UserId = userId,
                Stars = stars,
                UpdatedDate = DateTime.UtcNow
            });
        }
        else
        {
            existing.Stars = stars;
            existing.UpdatedDate = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        cache.RemoveByPrefix(CacheKeys.SmartPlaylistTracksPrefix);

        return true;
    }
}
