namespace Plaat.Contract.Models;

/// <summary>
/// The current user's star rating for one track. Ratings are per user - the same track can carry a
/// different rating for every account - which is why they are fetched separately from
/// <see cref="TrackDto"/>/<see cref="TrackDetailsDto"/> rather than embedded in them: those are
/// served from a process-wide cache shared by all users.
/// </summary>
public sealed class TrackRatingDto
{
    /// <summary>
    /// Lowest rating - also what an unrated track reads as.
    /// </summary>
    public const int MinStars = 0;

    /// <summary>
    /// Highest rating.
    /// </summary>
    public const int MaxStars = 5;

    public required uint TrackId { get; init; }

    /// <summary>
    /// Between <see cref="MinStars"/> and <see cref="MaxStars"/> inclusive.
    /// </summary>
    public required int Stars { get; init; }
}

/// <summary>
/// Request body for setting the current user's star rating on a track. <see cref="Stars"/> must be
/// between <see cref="TrackRatingDto.MinStars"/> and <see cref="TrackRatingDto.MaxStars"/>;
/// <see cref="TrackRatingDto.MinStars"/> clears the rating.
/// </summary>
public sealed record SetTrackRatingRequest(int Stars);
